import 'constants.dart';

class DiscoveryMessage {
  final String version;
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final String platform;
  final int port;
  final List<String> capabilities;
  final String protocolVersion;
  final int timestamp;

  const DiscoveryMessage({
    this.version = AppConstants.appVersion,
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    this.platform = 'android',
    this.port = AppConstants.sessionTcpPort,
    this.capabilities = const [
      'file_transfer',
      'clipboard',
      'remote_control',
      'backup',
    ],
    this.protocolVersion = AppConstants.protocolVersion,
    required this.timestamp,
  });

  Map<String, dynamic> toJson() => {
    'version': version,
    'deviceId': deviceId,
    'name': name,
    'manufacturer': manufacturer,
    'model': model,
    'platform': platform,
    'port': port,
    'capabilities': capabilities,
    'protocolVersion': protocolVersion,
    'timestamp': timestamp,
  };

  factory DiscoveryMessage.fromJson(Map<String, dynamic> json) {
    return DiscoveryMessage(
      version: json['version'] as String? ?? AppConstants.appVersion,
      deviceId: json['deviceId'] as String,
      name: json['name'] as String,
      manufacturer: json['manufacturer'] as String? ?? '',
      model: json['model'] as String? ?? '',
      platform: json['platform'] as String? ?? 'android',
      port: json['port'] as int? ?? AppConstants.sessionTcpPort,
      capabilities: List<String>.from(json['capabilities'] as List? ?? []),
      protocolVersion:
          json['protocolVersion'] as String? ?? AppConstants.protocolVersion,
      timestamp: json['timestamp'] as int? ?? 0,
    );
  }
}
