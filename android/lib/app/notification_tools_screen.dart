import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../core/device_model.dart';
import '../core/notification_manager.dart';
import '../core/notification_model.dart';
import '../core/ocr_engine.dart';
import '../core/pdf_tools.dart';
import '../core/session_manager.dart';

class NotificationToolsScreen extends StatefulWidget {
  final Device device;
  final SessionConnection connection;
  final NotificationManager? notificationManager;
  final OcrEngine? ocrEngine;

  const NotificationToolsScreen({
    super.key,
    required this.device,
    required this.connection,
    this.notificationManager,
    this.ocrEngine,
  });

  @override
  State<NotificationToolsScreen> createState() =>
      _NotificationToolsScreenState();
}

class _NotificationToolsScreenState extends State<NotificationToolsScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabController;
  late final NotificationManager _notifManager;
  late final OcrEngine _ocrEngine;

  final _titleController = TextEditingController(text: '会议提醒');
  final _contentController = TextEditingController(text: '下午 3:00 项目架构技术评审会');
  final _pkgController = TextEditingController(text: 'com.hinge.office');

  final List<NotificationEventMessage> _log = [];
  final List<String> _toolOutput = [];

  Uint8List? _samplePdf;
  String _pdfStatus = '尚未生成 PDF 文档';
  String _ocrStatus = '点击“执行 OCR 文字识别”测试原生文本提取';

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 3, vsync: this);
    _notifManager = widget.notificationManager ?? NotificationManager();
    _ocrEngine = widget.ocrEngine ?? MockOcrEngine();

    _notifManager.registerConnection(widget.connection);

    _notifManager.notificationStream.listen((notif) {
      if (mounted) {
        setState(() => _log.insert(0, notif));
      }
    });

    _notifManager.actionStream.listen((action) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              '收到远程操作指令: ${action.actionKey} (快捷回复: ${action.replyText ?? "无"})',
            ),
          ),
        );
      }
    });

    widget.connection.frames.listen((frame) {
      _notifManager.handleIncomingFrame(widget.connection, frame);
    });
  }

  @override
  void dispose() {
    _tabController.dispose();
    _titleController.dispose();
    _contentController.dispose();
    _pkgController.dispose();
    if (widget.notificationManager == null) {
      _notifManager.dispose();
    }
    super.dispose();
  }

  Future<void> _dispatchTestNotification() async {
    final notif = NotificationEventMessage(
      packageName: _pkgController.text.trim(),
      appName: 'Hinge',
      title: _titleController.text.trim(),
      content: _contentController.text.trim(),
      canReply: true,
      actions: ['reply', 'dismiss'],
    );

    final sent = await _notifManager.dispatchNotification(notif);
    if (!mounted) return;
    if (sent) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('通知已成功推送到远程 Windows 桌面！')));
    } else {
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('通知已被防骚扰规则或 2 秒防抖拦截抑制')));
    }
  }

  void _testPdfGenerate() {
    final pdf = PdfTools.createDocument('季度工作报告', [
      '第一页: 项目概况与基础架构规范',
      '第二页: 垂直切片与局域网传输性能',
      '第三页: 原生无外部依赖交付实践',
    ]);
    final meta = PdfTools.inspectMetadata(pdf);
    setState(() {
      _samplePdf = pdf;
      _pdfStatus =
          '已生成 PDF: ${meta.pageCount} 页, ${meta.fileSizeBytes} 字节, 规范版本: ${meta.version}';
      _toolOutput.insert(0, '[PDF 创建] 成功生成 3 页 PDF (${pdf.length} 字节)');
    });
  }

  void _testPdfMerge() {
    if (_samplePdf == null) _testPdfGenerate();
    final doc2 = PdfTools.createDocument('技术附录', ['附录 A: 跨端通信架构协议']);
    final merged = PdfTools.mergePdfs([_samplePdf!, doc2], '合并报告总览');
    final meta = PdfTools.inspectMetadata(merged);
    setState(() {
      _samplePdf = merged;
      _pdfStatus = '已合并 PDF: ${meta.pageCount} 页, ${meta.fileSizeBytes} 字节';
      _toolOutput.insert(0, '[PDF 合并] 成功将 2 份文档合并为 ${meta.pageCount} 页 PDF');
    });
  }

  void _testPdfSplit() {
    if (_samplePdf == null) _testPdfGenerate();
    final split = PdfTools.splitPdf(_samplePdf!, 1, 2);
    final meta = PdfTools.inspectMetadata(split);
    setState(() {
      _samplePdf = split;
      _pdfStatus = '已切分 PDF: ${meta.pageCount} 页, ${meta.fileSizeBytes} 字节';
      _toolOutput.insert(0, '[PDF 切分] 成功截取第 1-2 页生成独立 PDF');
    });
  }

  Future<void> _testOcr() async {
    final dummyImage = Uint8List.fromList([
      0xFF,
      0xD8,
      0xFF,
      0xE0,
      0x00,
      0x10,
      0x4A,
      0x46,
    ]);
    final result = await _ocrEngine.recognizeText(dummyImage);
    setState(() {
      _ocrStatus = result.success
          ? 'OCR 识别结果 (${result.elapsed.inMilliseconds}毫秒):\n${result.text}'
          : 'OCR 错误: ${result.errorMessage}';
      _toolOutput.insert(0, '[OCR] 提取到文字: "${result.text}"');
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text('工具与通知接力: ${widget.device.name}'),
        bottom: TabBar(
          controller: _tabController,
          tabs: const [
            Tab(icon: Icon(Icons.notifications_active), text: '通知接力'),
            Tab(icon: Icon(Icons.picture_as_pdf), text: '原生 PDF'),
            Tab(icon: Icon(Icons.document_scanner), text: '离线 OCR'),
          ],
        ),
      ),
      body: TabBarView(
        controller: _tabController,
        children: [
          // 1. Notification Relay
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          '向 Windows 电脑发送测试通知',
                          style: TextStyle(fontWeight: FontWeight.bold),
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _titleController,
                          decoration: const InputDecoration(
                            labelText: '通知标题',
                            isDense: true,
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: _contentController,
                          decoration: const InputDecoration(
                            labelText: '通知内容',
                            isDense: true,
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: _pkgController,
                          decoration: const InputDecoration(
                            labelText: '应用包名',
                            isDense: true,
                          ),
                        ),
                        const SizedBox(height: 12),
                        FilledButton.icon(
                          onPressed: _dispatchTestNotification,
                          icon: const Icon(Icons.send),
                          label: const Text('发送通知'),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                const Text(
                  '通知历史记录',
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: _log.isEmpty
                      ? const Center(child: Text('暂无通知发送记录'))
                      : ListView.builder(
                          itemCount: _log.length,
                          itemBuilder: (ctx, i) {
                            final item = _log[i];
                            return ListTile(
                              leading: const Icon(Icons.notifications),
                              title: Text(item.title),
                              subtitle: Text(
                                '${item.content} (${item.packageName})',
                              ),
                              trailing: item.canReply
                                  ? const Chip(
                                      label: Text(
                                        '支持回复',
                                        style: TextStyle(fontSize: 10),
                                      ),
                                    )
                                  : null,
                            );
                          },
                        ),
                ),
              ],
            ),
          ),

          // 2. Native PDF Tools
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        const Text(
                          '零依赖原生 PDF 核心操作',
                          style: TextStyle(fontWeight: FontWeight.bold),
                        ),
                        const SizedBox(height: 12),
                        Text(
                          _pdfStatus,
                          style: TextStyle(
                            color: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant,
                          ),
                        ),
                        const SizedBox(height: 16),
                        Wrap(
                          spacing: 12,
                          runSpacing: 12,
                          children: [
                            FilledButton.tonalIcon(
                              onPressed: _testPdfGenerate,
                              icon: const Icon(Icons.note_add),
                              label: const Text('生成 3 页 PDF'),
                            ),
                            FilledButton.tonalIcon(
                              onPressed: _testPdfMerge,
                              icon: const Icon(Icons.merge_type),
                              label: const Text('合并附录文档'),
                            ),
                            FilledButton.tonalIcon(
                              onPressed: _testPdfSplit,
                              icon: const Icon(Icons.call_split),
                              label: const Text('切分第 1-2 页'),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16),
                const Text(
                  '工具操作历史日志',
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
                const SizedBox(height: 8),
                Expanded(
                  child: ListView.builder(
                    itemCount: _toolOutput.length,
                    itemBuilder: (ctx, i) => ListTile(
                      dense: true,
                      leading: Icon(
                        Icons.check_circle,
                        size: 16,
                        color: Theme.of(ctx).colorScheme.primary,
                      ),
                      title: Text(
                        _toolOutput[i],
                        style: const TextStyle(fontSize: 13),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),

          // 3. Native OCR Tools
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text(
                          '离线 OCR 文字识别引擎',
                          style: TextStyle(fontWeight: FontWeight.bold),
                        ),
                        const SizedBox(height: 12),
                        Container(
                          padding: const EdgeInsets.all(12),
                          decoration: BoxDecoration(
                            color: Theme.of(context)
                                .colorScheme
                                .surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: Text(
                            _ocrStatus,
                            style: const TextStyle(fontFamily: 'monospace'),
                          ),
                        ),
                        const SizedBox(height: 16),
                        FilledButton.icon(
                          onPressed: _testOcr,
                          icon: const Icon(Icons.document_scanner),
                          label: const Text('执行 OCR 文字识别'),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
