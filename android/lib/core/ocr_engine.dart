import 'dart:convert';
import 'dart:typed_data';

class OcrResult {
  final bool success;
  final String text;
  final String? errorMessage;
  final Duration elapsed;

  const OcrResult({
    required this.success,
    this.text = '',
    this.errorMessage,
    required this.elapsed,
  });
}

abstract class OcrEngine {
  bool get isAvailable;
  Future<OcrResult> recognizeText(
    Uint8List imageBytes, {
    String language = 'en-US',
  });
}

class MockOcrEngine implements OcrEngine {
  @override
  bool get isAvailable => true;

  String mockRecognizedText =
      'Hinge Native OCR: Clean text recognized from sample image.';

  @override
  Future<OcrResult> recognizeText(
    Uint8List imageBytes, {
    String language = 'en-US',
  }) async {
    final sw = Stopwatch()..start();
    if (imageBytes.isEmpty) {
      return OcrResult(
        success: false,
        errorMessage: 'Image payload is empty or invalid.',
        elapsed: sw.elapsed,
      );
    }

    sw.stop();
    return OcrResult(
      success: true,
      text: mockRecognizedText,
      elapsed: sw.elapsed,
    );
  }
}

class ToolCommandMessage {
  final String commandId;
  final String toolType;
  final Map<String, String> parameters;

  ToolCommandMessage({
    String? commandId,
    required this.toolType,
    Map<String, String>? parameters,
  }) : commandId =
           commandId ?? DateTime.now().microsecondsSinceEpoch.toString(),
       parameters = parameters ?? {};

  Map<String, dynamic> toJson() => {
    'commandId': commandId,
    'toolType': toolType,
    'parameters': parameters,
  };

  String serialize() => jsonEncode(toJson());

  static ToolCommandMessage? fromJson(String jsonStr) {
    try {
      final map = jsonDecode(jsonStr) as Map<String, dynamic>;
      return ToolCommandMessage(
        commandId: map['commandId'] as String?,
        toolType: map['toolType'] as String? ?? '',
        parameters: (map['parameters'] as Map<String, dynamic>?)?.map(
          (k, v) => MapEntry(k, v.toString()),
        ),
      );
    } catch (_) {
      return null;
    }
  }
}

class ToolResultMessage {
  final String commandId;
  final bool success;
  final String? resultText;
  final String? errorMessage;

  ToolResultMessage({
    required this.commandId,
    required this.success,
    this.resultText,
    this.errorMessage,
  });

  Map<String, dynamic> toJson() {
    final map = <String, dynamic>{'commandId': commandId, 'success': success};
    if (resultText != null) map['resultText'] = resultText;
    if (errorMessage != null) map['errorMessage'] = errorMessage;
    return map;
  }

  String serialize() => jsonEncode(toJson());

  static ToolResultMessage? fromJson(String jsonStr) {
    try {
      final map = jsonDecode(jsonStr) as Map<String, dynamic>;
      return ToolResultMessage(
        commandId: map['commandId'] as String? ?? '',
        success: map['success'] as bool? ?? false,
        resultText: map['resultText'] as String?,
        errorMessage: map['errorMessage'] as String?,
      );
    } catch (_) {
      return null;
    }
  }
}
