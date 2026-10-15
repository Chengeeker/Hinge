import pathlib
import subprocess

repo = pathlib.Path(__file__).resolve().parents[1]
cache = pathlib.Path.home() / '.gradle/caches/modules-2/files-2.1'
work = repo / 'tmp/event-bridge-selfcheck'
work.mkdir(exist_ok=True)
native_root = 'android/android/app/src/main/kotlin/com/hinge/office/'

def jar(group, artifact, preferred):
    base = cache / group / artifact
    matches = list((base / preferred).glob('**/*.jar'))
    if not matches:
        matches = sorted(base.glob('*/*/*.jar'), reverse=True)
    return str(matches[0]) if matches else None

compiler_jars = [jar(*args) for args in [
    ('org.jetbrains.kotlin', 'kotlin-compiler-embeddable', '2.4.0'),
    ('org.jetbrains.kotlin', 'kotlin-stdlib', '2.4.0'),
    ('org.jetbrains.kotlin', 'kotlin-script-runtime', '2.4.0'),
    ('org.jetbrains.kotlin', 'kotlin-reflect', '1.6.10'),
    ('org.jetbrains.kotlin', 'kotlin-daemon-embeddable', '2.4.0'),
    ('org.jetbrains.kotlinx', 'kotlinx-coroutines-core-jvm', '1.10.2'),
    ('org.jetbrains', 'annotations', '13.0'),
    ('org.jetbrains.intellij.deps', 'trove4j', '1.0.20200330'),
]]
classpath = ';'.join(x for x in compiler_jars if x)
java = 'D:/jdk17/bin/java.exe'
(work / 'AndroidStubs.kt').write_text('''package android.os
class Looper { companion object { fun getMainLooper() = Looper() } }
class Handler(looper: Looper) {
    fun post(runnable: Runnable): Boolean { runnable.run(); return true }
    fun postDelayed(runnable: Runnable, delay: Long) = true
    fun removeCallbacks(runnable: Runnable) {}
}
object SystemClock { fun elapsedRealtime() = System.nanoTime() / 1_000_000 }
''', encoding='utf-8')
(work / 'FlutterStub.kt').write_text('''package io.flutter.plugin.common
class EventChannel { interface EventSink { fun success(event: Any?) } }
''', encoding='utf-8')
(work / 'Check.kt').write_text('''package com.hinge.office
import io.flutter.plugin.common.EventChannel
class Sink : EventChannel.EventSink {
    val received = mutableListOf<Any?>()
    override fun success(event: Any?) { received.add(event) }
}
fun checkBridge(name: String, attach: (EventChannel.EventSink?) -> Unit,
                detach: (EventChannel.EventSink?) -> Unit,
                emit: (Map<String, Any?>) -> Unit, capacity: Int) {
    val a = Sink(); val b = Sink()
    attach(a); attach(b); detach(a)
    emit(mapOf("event" to "probe"))
    check(b.received.size == 1) { "$name: old Activity detached the new sink" }
    detach(b); emit(mapOf("event" to "queued")); attach(a)
    check(a.received.size == 1) { "$name: pending event was lost" }
    detach(b); emit(mapOf("event" to "resumed"))
    check(a.received.size == 2) { "$name: late cleanup detached resumed Activity" }
    detach(a)
    repeat(capacity + 2) { emit(mapOf("event" to "bounded", "index" to it)) }
    val c = Sink(); attach(c)
    check(c.received.size == capacity) { "$name: pending queue is not bounded" }
    detach(c)
    println("PASS $name: ownership, resume, pending delivery, bounded queue")
}
fun main() {
    checkBridge("session", HingeNativeConnectionEvents::attach,
        HingeNativeConnectionEvents::detach, HingeNativeConnectionEvents::emit, 128)
    checkBridge("sms", SmsRelayBridge::attach, SmsRelayBridge::detach,
        SmsRelayBridge::emit, 8)
    checkBridge("history", NotificationHistoryBridge::attach,
        NotificationHistoryBridge::detach, NotificationHistoryBridge::emit, 16)
}
''', encoding='utf-8')

def run(baseline):
    for filename in ['HingeNativeConnectionBroker.kt', 'SmsRelayBridge.kt', 'NotificationHistoryBridge.kt']:
        if baseline:
            source = subprocess.check_output(['git', '-c', f'safe.directory={repo.as_posix()}',
                'show', f'v2.0.1:{native_root}{filename}'], cwd=repo).decode('utf-8')
            source = source.replace('fun detach()', 'fun detach(expectedSink: EventChannel.EventSink?)')
        else:
            source = (repo / native_root / filename).read_text(encoding='utf-8')
        if filename == 'HingeNativeConnectionBroker.kt':
            source = ('package com.hinge.office\nimport io.flutter.plugin.common.EventChannel\n'
                      'import java.util.ArrayDeque\n' + source[source.index('object HingeNativeConnectionEvents'):source.index('private object HingeProtocol')])
        (work / filename).write_text(source, encoding='utf-8')
    output = work / ('baseline.jar' if baseline else 'patched.jar')
    args = [java, '-cp', classpath, 'org.jetbrains.kotlin.cli.jvm.K2JVMCompiler',
            '-no-stdlib', '-no-reflect', '-classpath', classpath,
            '-d', str(output), *map(str, work.glob('*.kt'))]
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode:
        print(result.stdout + result.stderr); raise SystemExit(result.returncode)
    result = subprocess.run([java, '-cp', f'{output};{classpath}', 'com.hinge.office.CheckKt'],
                            capture_output=True, text=True)
    print(('BASELINE' if baseline else 'PATCHED') + ': ' + result.stdout + result.stderr)
    if baseline:
        assert result.returncode != 0 and 'old Activity detached the new sink' in result.stderr
    else:
        assert result.returncode == 0

run(True)
run(False)
