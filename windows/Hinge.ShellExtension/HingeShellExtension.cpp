// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// The COM activation shape is adapted from Microsoft's
// vscode-explorer-command reference implementation. Hinge's dynamic device
// enumeration, registry snapshot and process activation are implemented here.

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <wrl/module.h>

#include <algorithm>
#include <limits>
#include <sstream>
#include <string>
#include <mutex>
#include <utility>
#include <vector>

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::InhibitRoOriginateError;
using Microsoft::WRL::Make;
using Microsoft::WRL::Module;
using Microsoft::WRL::ModuleType;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

namespace {

constexpr wchar_t kSnapshotPath[] = L"Software\\Hinge\\ExplorerSend";
constexpr wchar_t kDevicesSubkey[] = L"Devices";
constexpr wchar_t kRootTitle[] = L"通过 Hinge 发送到";
constexpr wchar_t kSnapshotFileName[] = L"explorer-send.txt";

struct DeviceEntry {
    std::wstring id;
    std::wstring name;
};

struct SnapshotData {
    std::wstring executable;
    std::vector<DeviceEntry> devices;
};

struct FileStamp {
    bool exists = false;
    FILETIME lastWrite{};
    ULONGLONG size = 0;
};

struct ExecutableCache {
    std::mutex mutex;
    bool initialized = false;
    bool fromRegistry = false;
    FILETIME registryLastWrite{};
    FileStamp fileStamp{};
    std::wstring path;
};

struct DevicesCache {
    std::mutex mutex;
    bool initialized = false;
    bool fromRegistry = false;
    FILETIME registryLastWrite{};
    FileStamp fileStamp{};
    std::vector<DeviceEntry> devices;
};

ExecutableCache& CachedExecutable() {
    static ExecutableCache cache;
    return cache;
}

DevicesCache& CachedDevices() {
    static DevicesCache cache;
    return cache;
}

bool SameFileTime(const FILETIME& left, const FILETIME& right) {
    return left.dwLowDateTime == right.dwLowDateTime &&
        left.dwHighDateTime == right.dwHighDateTime;
}

bool SameFileStamp(const FileStamp& left, const FileStamp& right) {
    return left.exists == right.exists &&
        left.size == right.size &&
        SameFileTime(left.lastWrite, right.lastWrite);
}

bool QueryLastWriteTime(HKEY key, FILETIME& lastWrite) {
    return key != nullptr && RegQueryInfoKeyW(
        key,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        &lastWrite) == ERROR_SUCCESS;
}

std::wstring SnapshotFilePath() {
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (length == 0) return {};
    std::wstring directory(length, L'\0');
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", directory.data(), length) == 0) return {};
    directory.resize(wcslen(directory.c_str()));
    return directory + L"\\Hinge\\" + kSnapshotFileName;
}

std::wstring Utf8ToWide(const std::string& value) {
    if (value.empty()) return {};
    const int length = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (length <= 0) return {};
    std::wstring result(length, L'\0');
    if (MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            length) != length) {
        return {};
    }
    return result;
}

std::string ReadUtf8File(const std::wstring& path) {
    HANDLE file = CreateFileW(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};

    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0 || size.QuadPart > 1024 * 1024) {
        CloseHandle(file);
        return {};
    }
    std::string contents(static_cast<size_t>(size.QuadPart), '\0');
    DWORD bytesRead = 0;
    const BOOL read = ReadFile(
        file,
        contents.data(),
        static_cast<DWORD>(contents.size()),
        &bytesRead,
        nullptr);
    CloseHandle(file);
    if (!read || bytesRead != contents.size()) return {};
    return contents;
}

FileStamp SnapshotFileStamp() {
    FileStamp stamp;
    const auto path = SnapshotFilePath();
    if (path.empty()) return stamp;

    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) {
        return stamp;
    }

    stamp.exists = true;
    stamp.lastWrite = data.ftLastWriteTime;
    stamp.size = (static_cast<ULONGLONG>(data.nFileSizeHigh) << 32) | data.nFileSizeLow;
    return stamp;
}

SnapshotData ReadSnapshotFile() {
    const auto path = SnapshotFilePath();
    if (path.empty()) return {};
    const auto text = Utf8ToWide(ReadUtf8File(path));
    if (text.empty()) return {};

    SnapshotData snapshot;
    std::wistringstream lines(text);
    std::wstring line;
    while (std::getline(lines, line)) {
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        const auto firstTab = line.find(L'\t');
        if (firstTab == std::wstring::npos) continue;
        const auto kind = line.substr(0, firstTab);
        if (kind == L"executable") {
            snapshot.executable = line.substr(firstTab + 1);
            continue;
        }
        if (kind != L"device") continue;
        const auto secondTab = line.find(L'\t', firstTab + 1);
        if (secondTab == std::wstring::npos) continue;
        DeviceEntry entry{
            line.substr(firstTab + 1, secondTab - firstTab - 1),
            line.substr(secondTab + 1),
        };
        if (!entry.id.empty()) snapshot.devices.push_back(std::move(entry));
    }

    std::sort(snapshot.devices.begin(), snapshot.devices.end(), [](const auto& left, const auto& right) {
        const int byName = _wcsicmp(left.name.c_str(), right.name.c_str());
        return byName == 0 ? _wcsicmp(left.id.c_str(), right.id.c_str()) < 0 : byName < 0;
    });
    snapshot.devices.erase(
        std::unique(snapshot.devices.begin(), snapshot.devices.end(), [](const auto& left, const auto& right) {
            return _wcsicmp(left.id.c_str(), right.id.c_str()) == 0;
        }),
        snapshot.devices.end());
    return snapshot;
}

std::wstring ReadStringValue(HKEY key, const wchar_t* name) {
    DWORD type = 0;
    DWORD bytes = 0;
    if (RegQueryValueExW(key, name, nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS ||
        (type != REG_SZ && type != REG_EXPAND_SZ) || bytes < sizeof(wchar_t)) {
        return {};
    }

    std::wstring value(bytes / sizeof(wchar_t), L'\0');
    if (RegQueryValueExW(
            key,
            name,
            nullptr,
            &type,
            reinterpret_cast<BYTE*>(value.data()),
            &bytes) != ERROR_SUCCESS) {
        return {};
    }
    while (!value.empty() && value.back() == L'\0') value.pop_back();
    return value;
}

std::wstring ReadExecutablePath() {
    auto& cache = CachedExecutable();
    std::lock_guard<std::mutex> guard(cache.mutex);

    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, kSnapshotPath, 0, KEY_READ, &key) == ERROR_SUCCESS) {
        FILETIME lastWrite{};
        const bool hasStamp = QueryLastWriteTime(key, lastWrite);
        const auto value = ReadStringValue(key, L"ExecutablePath");
        RegCloseKey(key);
        if (!value.empty()) {
            if (cache.initialized && cache.fromRegistry &&
                (!hasStamp || SameFileTime(cache.registryLastWrite, lastWrite))) {
                return cache.path;
            }

            cache.initialized = true;
            cache.fromRegistry = true;
            cache.registryLastWrite = lastWrite;
            cache.fileStamp = {};
            cache.path = value;
            return cache.path;
        }
    }

    const auto fileStamp = SnapshotFileStamp();
    if (cache.initialized && !cache.fromRegistry && SameFileStamp(cache.fileStamp, fileStamp)) {
        return cache.path;
    }

    cache.initialized = true;
    cache.fromRegistry = false;
    cache.registryLastWrite = {};
    cache.fileStamp = fileStamp;
    cache.path = ReadSnapshotFile().executable;
    return cache.path;
}

std::vector<DeviceEntry> ReadDevices() {
    auto& cache = CachedDevices();
    std::lock_guard<std::mutex> guard(cache.mutex);

    std::vector<DeviceEntry> result;
    HKEY parent = nullptr;
    std::wstring path = std::wstring(kSnapshotPath) + L"\\" + kDevicesSubkey;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, path.c_str(), 0, KEY_READ, &parent) == ERROR_SUCCESS) {
        FILETIME lastWrite{};
        const bool hasStamp = QueryLastWriteTime(parent, lastWrite);
        if (cache.initialized && cache.fromRegistry &&
            (!hasStamp || SameFileTime(cache.registryLastWrite, lastWrite))) {
            return cache.devices;
        }

        DWORD index = 0;
        for (;;) {
            wchar_t subkeyName[256]{};
            DWORD nameLength = static_cast<DWORD>(std::size(subkeyName));
            const LONG enumResult = RegEnumKeyExW(
                parent, index++, subkeyName, &nameLength, nullptr, nullptr, nullptr, nullptr);
            if (enumResult == ERROR_NO_MORE_ITEMS) break;
            if (enumResult != ERROR_SUCCESS) continue;

            HKEY child = nullptr;
            if (RegOpenKeyExW(parent, subkeyName, 0, KEY_READ, &child) != ERROR_SUCCESS) continue;
            DeviceEntry entry{ReadStringValue(child, L"DeviceId"), ReadStringValue(child, L"Name")};
            RegCloseKey(child);
            if (entry.id.empty()) continue;
            if (entry.name.empty()) entry.name = L"已连接设备";
            result.push_back(std::move(entry));
        }
        RegCloseKey(parent);

        // Preserve the existing file fallback for the short window where the
        // registry key exists but its child records have not been published.
        if (result.empty()) result = ReadSnapshotFile().devices;

        std::sort(result.begin(), result.end(), [](const auto& left, const auto& right) {
            const int byName = _wcsicmp(left.name.c_str(), right.name.c_str());
            return byName == 0 ? _wcsicmp(left.id.c_str(), right.id.c_str()) < 0 : byName < 0;
        });
        result.erase(
            std::unique(result.begin(), result.end(), [](const auto& left, const auto& right) {
                return _wcsicmp(left.id.c_str(), right.id.c_str()) == 0;
            }),
            result.end());

        cache.initialized = true;
        cache.fromRegistry = true;
        cache.registryLastWrite = lastWrite;
        cache.fileStamp = {};
        cache.devices = result;
        return cache.devices;
    }

    const auto fileStamp = SnapshotFileStamp();
    if (cache.initialized && !cache.fromRegistry && SameFileStamp(cache.fileStamp, fileStamp)) {
        return cache.devices;
    }

    cache.initialized = true;
    cache.fromRegistry = false;
    cache.registryLastWrite = {};
    cache.fileStamp = fileStamp;
    cache.devices = ReadSnapshotFile().devices;
    return cache.devices;
}

std::wstring QuoteArgument(const std::wstring& value) {
    if (value.find_first_of(L" \t\n\v\"") == std::wstring::npos) return value;
    std::wstring output(1, L'"');
    size_t slashes = 0;
    for (const wchar_t character : value) {
        if (character == L'\\') {
            ++slashes;
        } else if (character == L'"') {
            output.append(slashes * 2 + 1, L'\\');
            output.push_back(L'"');
            slashes = 0;
        } else {
            output.append(slashes, L'\\');
            slashes = 0;
            output.push_back(character);
        }
    }
    output.append(slashes * 2, L'\\');
    output.push_back(L'"');
    return output;
}

std::string WideToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int byteCount = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (byteCount <= 0) return {};

    std::string result(static_cast<size_t>(byteCount), '\0');
    if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            byteCount,
            nullptr,
            nullptr) != byteCount) {
        return {};
    }
    return result;
}

constexpr wchar_t kShellSendPipeName[] = L"\\\\.\\pipe\\Hinge.ShellSend.v1";

bool SendShellRequestToRunningHinge(const std::wstring& arguments) {
    const auto payload = WideToUtf8(arguments);
    if (payload.empty() || payload.size() > std::numeric_limits<DWORD>::max()) {
        return false;
    }

    const DWORD payloadSize = static_cast<DWORD>(payload.size());
    for (int attempt = 0; attempt < 8; ++attempt) {
        if (!WaitNamedPipeW(kShellSendPipeName, 75)) {
            Sleep(20);
            continue;
        }

        HANDLE pipe = CreateFileW(
            kShellSendPipeName,
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            Sleep(20);
            continue;
        }

        DWORD written = 0;
        const BOOL writeSucceeded = WriteFile(
            pipe,
            payload.data(),
            payloadSize,
            &written,
            nullptr);
        // The Hinge server reads this one-shot request with ReadToEndAsync.
        // FlushFileBuffers waits for the server to consume buffered bytes,
        // while ReadToEndAsync waits for this client handle to close. Calling
        // both creates a cross-process deadlock in Explorer's dllhost.exe.
        // Closing the handle after a successful synchronous WriteFile is the
        // request terminator and is sufficient for this local named pipe.
        CloseHandle(pipe);

        if (writeSucceeded && written == payloadSize) return true;
        Sleep(20);
    }
    return false;
}

HRESULT DuplicateString(const std::wstring& value, PWSTR* output) {
    if (!output) return E_POINTER;
    *output = nullptr;
    return SHStrDupW(value.c_str(), output);
}

HRESULT LaunchHinge(const std::wstring& deviceId, IShellItemArray* items) {
    if (!items) return E_INVALIDARG;

    DWORD count = 0;
    HRESULT result = items->GetCount(&count);
    if (FAILED(result) || count == 0) return FAILED(result) ? result : E_INVALIDARG;

    std::wstring arguments =
        L"--silent --shell-send --device-id " + QuoteArgument(deviceId);
    DWORD pathCount = 0;
    for (DWORD index = 0; index < count; ++index) {
        ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(index, &item))) continue;
        PWSTR rawPath = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath)) && rawPath) {
            arguments.push_back(L' ');
            arguments += QuoteArgument(rawPath);
            ++pathCount;
        }
        CoTaskMemFree(rawPath);
    }
    if (pathCount == 0) return E_INVALIDARG;

    // Prefer the already resident Hinge process. Starting another process
    // makes the shell command appear to work while the actual send request is
    // lost during AppInstance activation redirection.
    if (SendShellRequestToRunningHinge(arguments)) return S_OK;

    const auto executable = ReadExecutablePath();
    if (executable.empty() || GetFileAttributesW(executable.c_str()) == INVALID_FILE_ATTRIBUTES) {
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    std::wstring command = QuoteArgument(executable) + L" " + arguments;

    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    STARTUPINFOW startupInfo{sizeof(startupInfo)};
    PROCESS_INFORMATION processInfo{};
    if (!CreateProcessW(
            executable.c_str(),
            mutableCommand.data(),
            nullptr,
            nullptr,
            FALSE,
            CREATE_UNICODE_ENVIRONMENT,
            nullptr,
            nullptr,
            &startupInfo,
            &processInfo)) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    return S_OK;
}

class DeviceCommand final : public RuntimeClass<
    RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IExplorerCommand> {
public:
    DeviceCommand(std::wstring id, std::wstring name)
        : id_(std::move(id)),
          name_(name.empty() ? std::wstring(L"请先连接设备") : std::move(name)) {}

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override {
        return DuplicateString(name_, name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        const auto executable = ReadExecutablePath();
        return executable.empty() ? E_NOTIMPL : DuplicateString(executable, icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* infoTip) override {
        if (!infoTip) return E_POINTER;
        *infoTip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override {
        if (!guid) return E_POINTER;
        *guid = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override {
        if (!state) return E_POINTER;
        *state = id_.empty() ? ECS_DISABLED : ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override {
        if (id_.empty()) return HRESULT_FROM_WIN32(ERROR_NOT_CONNECTED);
        return LaunchHinge(id_, items);
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override {
        if (!flags) return E_POINTER;
        *flags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override {
        if (!commands) return E_POINTER;
        *commands = nullptr;
        return E_NOTIMPL;
    }

private:
    std::wstring id_;
    std::wstring name_;
};

class CommandEnumerator final : public RuntimeClass<
    RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IEnumExplorerCommand> {
public:
    explicit CommandEnumerator(std::vector<DeviceEntry> devices) {
        commands_.reserve(devices.size());
        for (auto& device : devices) {
            auto command = Make<DeviceCommand>(std::move(device.id), std::move(device.name));
            if (command) commands_.push_back(command);
        }
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override {
        if (!commands || (count > 1 && !fetched)) return E_POINTER;
        ULONG written = 0;
        while (written < count && position_ < commands_.size()) {
            commands[written] = commands_[position_].Get();
            commands[written]->AddRef();
            ++written;
            ++position_;
        }
        if (fetched) *fetched = written;
        return written == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG count) override {
        const size_t remaining = commands_.size() - position_;
        const size_t skipped = (std::min)(remaining, static_cast<size_t>(count));
        position_ += skipped;
        return skipped == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override {
        position_ = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override {
        if (!clone) return E_POINTER;
        *clone = nullptr;
        auto copy = Make<CommandEnumerator>(std::vector<DeviceEntry>{});
        if (!copy) return E_OUTOFMEMORY;
        copy->commands_ = commands_;
        copy->position_ = position_;
        return copy.CopyTo(clone);
    }

private:
    std::vector<ComPtr<IExplorerCommand>> commands_;
    size_t position_ = 0;
};

} // namespace

class __declspec(uuid("C2F9A27D-27C4-48E4-9E35-589B89A7CE21")) HingeExplorerCommand final
    : public RuntimeClass<
          RuntimeClassFlags<ClassicCom | InhibitRoOriginateError>, IExplorerCommand> {
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override {
        return DuplicateString(kRootTitle, name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        const auto executable = ReadExecutablePath();
        return executable.empty() ? E_NOTIMPL : DuplicateString(executable, icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* infoTip) override {
        if (!infoTip) return E_POINTER;
        *infoTip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override {
        if (!guid) return E_POINTER;
        *guid = __uuidof(HingeExplorerCommand);
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override {
        if (!state) return E_POINTER;
        // Keep the root item visible even while the app is disconnected. A
        // disabled child explains the state, and a transient registry/COM
        // snapshot read must not make the first-level menu disappear.
        *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override {
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override {
        if (!flags) return E_POINTER;
        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override {
        if (!commands) return E_POINTER;
        *commands = nullptr;
        auto devices = ReadDevices();
        if (devices.empty()) {
            devices.push_back(DeviceEntry{});
        }
        auto enumerator = Make<CommandEnumerator>(std::move(devices));
        return enumerator ? enumerator.CopyTo(commands) : E_OUTOFMEMORY;
    }
};

CoCreatableClass(HingeExplorerCommand)
CoCreatableClassWrlCreatorMapInclude(HingeExplorerCommand)

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID classId, REFIID interfaceId, void** result) {
    if (!result) return E_POINTER;
    *result = nullptr;
    return Module<ModuleType::InProc>::GetModule().GetClassObject(classId, interfaceId, result);
}

STDAPI DllCanUnloadNow() {
    return Module<ModuleType::InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetActivationFactory(HSTRING classId, IActivationFactory** factory) {
    return Module<ModuleType::InProc>::GetModule().GetActivationFactory(classId, factory);
}
