import 'constants.dart';

class DiscoveryMessage {
  final String version;
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final String platform;
  final int port;
  final int discoveryPort;
  final List<String> capabilities;
  final String protocolVersion;
  final int timestamp;
  final bool connectionRequested;
  final bool automaticReconnect;

  const DiscoveryMessage({
    this.version = AppConstants.appVersion,
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    this.platform = 'android',
    this.port = AppConstants.sessionTcpPort,
    this.discoveryPort = AppConstants.discoveryUdpPort,
    this.capabilities = const [
      'file_transfer',
      'clipboard',
      'remote_control',
      'backup',
    ],
    this.protocolVersion = AppConstants.protocolVersion,
    required this.timestamp,
    this.connectionRequested = false,
    this.automaticReconnect = false,
  });

  Map<String, dynamic> toJson() => {
    'version': version,
    'deviceId': deviceId,
    'name': name,
    'manufacturer': manufacturer,
    'model': model,
    'platform': platform,
    'port': port,
    'discoveryPort': discoveryPort,
    'capabilities': capabilities,
    'protocolVersion': protocolVersion,
    'timestamp': timestamp,
    'connectionRequested': connectionRequested,
    'automaticReconnect': automaticReconnect,
  };

  factory DiscoveryMessage.fromJson(Map<String, dynamic> json) {
    return DiscoveryMessage(
      version: json['version'] as String? ?? AppConstants.appVersion,
      deviceId: json['deviceId'] as String,
      name: json['name'] as String,
      manufacturer: json['manufacturer'] as String? ?? '',
      model: json['model'] as String? ?? '',
      platform: json['platform'] as String? ?? 'android',
      port: parseNetworkPort(json['port'], AppConstants.sessionTcpPort),
      discoveryPort: parseNetworkPort(
        json['discoveryPort'],
        AppConstants.discoveryUdpPort,
      ),
      capabilities: List<String>.from(json['capabilities'] as List? ?? []),
      protocolVersion:
          json['protocolVersion'] as String? ?? AppConstants.protocolVersion,
      timestamp: json['timestamp'] as int? ?? 0,
      connectionRequested: json['connectionRequested'] as bool? ?? false,
      automaticReconnect: json['automaticReconnect'] as bool? ?? false,
    );
  }
}

int parseNetworkPort(Object? value, int fallback) {
  final port = value is num
      ? value.toInt()
      : int.tryParse(value?.toString() ?? '');
  return port != null && port > 0 && port <= 65535 ? port : fallback;
}
