import 'constants.dart';
import 'discovery_message.dart';

enum DevicePlatform { android, windows, linux, macos, ios, unknown }

enum DeviceConnectionState {
  disconnected,
  discovered,
  connecting,
  authenticating,
  connected,
  reconnecting,
}

enum DeviceTrustState { untrusted, pendingVerification, trusted, blocked }

class Device {
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final DevicePlatform platform;
  final String appVersion;
  final String protocolVersion;
  final int sessionPort;
  final int discoveryPort;
  final List<String> capabilities;
  final List<String> networkAddresses;
  final DeviceConnectionState connectionState;
  final DeviceTrustState trustState;

  const Device({
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    required this.platform,
    required this.appVersion,
    required this.protocolVersion,
    this.sessionPort = AppConstants.sessionTcpPort,
    this.discoveryPort = AppConstants.discoveryUdpPort,
    required this.capabilities,
    required this.networkAddresses,
    this.connectionState = DeviceConnectionState.discovered,
    this.trustState = DeviceTrustState.untrusted,
  });

  Device copyWith({
    String? deviceId,
    String? name,
    String? manufacturer,
    String? model,
    DevicePlatform? platform,
    String? appVersion,
    String? protocolVersion,
    int? sessionPort,
    int? discoveryPort,
    List<String>? capabilities,
    List<String>? networkAddresses,
    DeviceConnectionState? connectionState,
    DeviceTrustState? trustState,
  }) {
    return Device(
      deviceId: deviceId ?? this.deviceId,
      name: name ?? this.name,
      manufacturer: manufacturer ?? this.manufacturer,
      model: model ?? this.model,
      platform: platform ?? this.platform,
      appVersion: appVersion ?? this.appVersion,
      protocolVersion: protocolVersion ?? this.protocolVersion,
      sessionPort: sessionPort ?? this.sessionPort,
      discoveryPort: discoveryPort ?? this.discoveryPort,
      capabilities: capabilities ?? this.capabilities,
      networkAddresses: networkAddresses ?? this.networkAddresses,
      connectionState: connectionState ?? this.connectionState,
      trustState: trustState ?? this.trustState,
    );
  }

  Map<String, dynamic> toJson() => {
    'deviceId': deviceId,
    'name': name,
    'manufacturer': manufacturer,
    'model': model,
    'platform': platform.name,
    'appVersion': appVersion,
    'protocolVersion': protocolVersion,
    'port': sessionPort,
    'discoveryPort': discoveryPort,
    'capabilities': capabilities,
    'networkAddresses': networkAddresses,
    'connectionState': connectionState.name,
    'trustState': trustState.name,
  };

  factory Device.fromJson(Map<String, dynamic> json) {
    return Device(
      deviceId: json['deviceId'] as String,
      name: json['name'] as String,
      manufacturer: json['manufacturer'] as String? ?? '',
      model: json['model'] as String? ?? '',
      platform: DevicePlatform.values.firstWhere(
        (e) => e.name == json['platform'],
        orElse: () => DevicePlatform.unknown,
      ),
      appVersion: json['appVersion'] as String? ?? '1.0.1',
      protocolVersion: json['protocolVersion'] as String? ?? '0.1',
      sessionPort: parseNetworkPort(json['port'], AppConstants.sessionTcpPort),
      discoveryPort: parseNetworkPort(
        json['discoveryPort'],
        AppConstants.discoveryUdpPort,
      ),
      capabilities: List<String>.from(json['capabilities'] as List? ?? []),
      networkAddresses: List<String>.from(
        json['networkAddresses'] as List? ?? [],
      ),
      connectionState: DeviceConnectionState.values.firstWhere(
        (e) => e.name == json['connectionState'],
        orElse: () => DeviceConnectionState.discovered,
      ),
      trustState: DeviceTrustState.values.firstWhere(
        (e) => e.name == json['trustState'],
        orElse: () => DeviceTrustState.untrusted,
      ),
    );
  }
}
