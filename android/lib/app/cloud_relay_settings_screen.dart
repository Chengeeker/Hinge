import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/cloud_relay.dart';
import '../core/device_identity_manager.dart';
import '../core/workspace_state.dart';

class CloudRelaySettingsScreen extends StatefulWidget {
  final WorkspaceState workspaceState;
  final DeviceIdentity localIdentity;
  final String clientVersion;

  const CloudRelaySettingsScreen({
    super.key,
    required this.workspaceState,
    required this.localIdentity,
    required this.clientVersion,
  });

  @override
  State<CloudRelaySettingsScreen> createState() =>
      _CloudRelaySettingsScreenState();
}

class _CloudRelaySettingsScreenState extends State<CloudRelaySettingsScreen> {
  late final TextEditingController _endpointController;
  late final TextEditingController _adminTokenController;
  late final TextEditingController _keyController;
  bool _enabled = false;
  bool _busy = false;
  String _status = '';

  CloudRelaySettings get _settings => widget.workspaceState.cloudRelaySettings;

  @override
  void initState() {
    super.initState();
    _endpointController = TextEditingController(text: _settings.endpoint);
    _adminTokenController = TextEditingController();
    _keyController = TextEditingController(text: _settings.relayEncryptionKey);
    _enabled = _settings.enabled;
  }

  @override
  void dispose() {
    _endpointController.dispose();
    _adminTokenController.dispose();
    _keyController.dispose();
    super.dispose();
  }

  void _save({String? deviceToken}) {
    widget.workspaceState.setCloudRelaySettings(
      _settings.copyWith(
        enabled: _enabled,
        endpoint: _endpointController.text.trim(),
        deviceToken: deviceToken ?? _settings.deviceToken,
        relayEncryptionKey: _keyController.text.trim(),
      ),
    );
  }

  Future<void> _testEndpoint() async {
    final endpoint = _endpointController.text.trim();
    if (endpoint.isEmpty) {
      setState(() => _status = '请先填写 Worker 地址。');
      return;
    }
    setState(() {
      _busy = true;
      _status = '正在测试 Worker…';
    });
    final client = CloudRelayClient();
    try {
      final healthy = await client.checkHealth(endpoint);
      if (!mounted) return;
      setState(() => _status = healthy ? 'Worker 可用。' : 'Worker 返回了非成功状态。');
    } catch (error) {
      if (mounted) setState(() => _status = '测试失败：$error');
    } finally {
      client.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _registerDevice() async {
    final endpoint = _endpointController.text.trim();
    final adminToken = _adminTokenController.text.trim();
    final key = _keyController.text.trim();
    if (endpoint.isEmpty || adminToken.isEmpty || key.isEmpty) {
      setState(() => _status = '注册前请填写 Worker 地址、部署 Token 和 Relay 密钥。');
      return;
    }
    try {
      // Validate the key before sending any registration request.
      CloudRelayCrypto.computeRelayDeviceId(key, widget.localIdentity.deviceId);
    } catch (error) {
      setState(() => _status = 'Relay 密钥无效：$error');
      return;
    }
    setState(() {
      _busy = true;
      _status = '正在注册此设备…';
    });
    final client = CloudRelayClient();
    try {
      final relayDeviceId = CloudRelayCrypto.computeRelayDeviceId(
        key,
        widget.localIdentity.deviceId,
      );
      final credentials = await client.register(
        endpoint: endpoint,
        adminToken: adminToken,
        relayDeviceId: relayDeviceId,
        deviceName: widget.localIdentity.name,
        platform: 'android',
        clientVersion: widget.clientVersion,
      );
      _enabled = true;
      _save(deviceToken: credentials.deviceToken);
      _adminTokenController.clear();
      if (mounted) {
        setState(() => _status = '设备已注册；部署 Token 已从输入框清除。');
      }
    } catch (error) {
      if (mounted) setState(() => _status = '注册失败：$error');
    } finally {
      client.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  void _generateKey() {
    _keyController.text = CloudRelayCrypto.generateEncryptionKey();
    setState(() => _status = '已生成新密钥。请在两台已信任设备上使用同一密钥后再注册。');
  }

  Future<void> _copyKey() async {
    final key = _keyController.text.trim();
    if (key.isEmpty) return;
    await Clipboard.setData(ClipboardData(text: key));
    if (mounted) setState(() => _status = 'Relay 密钥已复制到系统剪贴板。');
  }

  @override
  Widget build(BuildContext context) {
    final configured = _settings.deviceToken.isNotEmpty;
    return Scaffold(
      appBar: AppBar(title: const Text('Cloud Relay')),
      body: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          const Text(
            '可选的异步文件中转',
            style: TextStyle(fontSize: 22, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 8),
          const Text(
            '局域网连接仍然优先。Cloud Relay 只对已经在 Hinge 中信任过的设备生效；Worker 和 R2 只保存加密文件。',
          ),
          const SizedBox(height: 16),
          Card(
            child: SwitchListTile.adaptive(
              title: const Text('启用 Cloud Relay'),
              subtitle: Text(
                configured ? '当前设备已注册，可在局域网不可用时使用' : '完成 Worker 注册后才能发送或接收',
              ),
              value: _enabled,
              onChanged: _busy
                  ? null
                  : (value) {
                      setState(() => _enabled = value);
                      _save();
                    },
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _endpointController,
            keyboardType: TextInputType.url,
            decoration: const InputDecoration(
              labelText: 'Worker 地址',
              hintText: 'https://hinge-relay.example.workers.dev',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _keyController,
            decoration: const InputDecoration(
              labelText: 'Relay 加密密钥',
              helperText: '两台已信任设备必须使用同一密钥；密钥不会上传 Worker。',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              OutlinedButton.icon(
                onPressed: _busy ? null : _generateKey,
                icon: const Icon(Icons.refresh),
                label: const Text('生成密钥'),
              ),
              OutlinedButton.icon(
                onPressed: _copyKey,
                icon: const Icon(Icons.copy),
                label: const Text('复制密钥'),
              ),
            ],
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _adminTokenController,
            obscureText: true,
            decoration: const InputDecoration(
              labelText: '部署 Token（仅注册时使用）',
              helperText: '注册完成后不会保存部署 Token；普通 API 使用设备 Token。',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              FilledButton.icon(
                onPressed: _busy ? null : _registerDevice,
                icon: const Icon(Icons.app_registration),
                label: Text(configured ? '重新注册此设备' : '注册此设备'),
              ),
              OutlinedButton(
                onPressed: _busy ? null : _testEndpoint,
                child: const Text('测试 Worker'),
              ),
              OutlinedButton(
                onPressed: _busy ? null : () {
                  _save();
                  setState(() => _status = 'Cloud Relay 配置已保存。');
                },
                child: const Text('保存配置'),
              ),
            ],
          ),
          if (_busy) ...[
            const SizedBox(height: 16),
            const LinearProgressIndicator(),
          ],
          if (_status.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text(_status),
          ],
          const SizedBox(height: 20),
          const Text(
            '注意：Cloud Relay 是“可靠送达”而不是保证即时唤醒。Android 被系统完全停止时，必须等 Hinge 再次运行或系统允许的任务触发后才会下载。',
          ),
        ],
      ),
    );
  }
}
