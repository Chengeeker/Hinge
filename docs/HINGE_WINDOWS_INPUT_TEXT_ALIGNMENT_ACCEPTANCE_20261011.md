# Hinge Windows input text alignment and product information (2026-10-11)

## Changes

- Root cause: the shared M3 `TextBox` and `PasswordBox` styles set minimum height and surfaces but did not set `VerticalContentAlignment`; taller controls therefore left their text at the template's default vertical position.
- Set `VerticalContentAlignment=Center` on the existing shared text and password field styles. This covers the storage path, local pairing code, wireless ADB pairing code, and the other controls using those styles without changing their sizes, padding, or input behavior.
- Removed the `WinUI 3` wording from the visible About page product-information line, keeping the Windows and Android Material 3 descriptions.

## Verification

- WinUI Release build succeeded with 0 warnings / 0 errors.
- Windows Release installer and portable ZIP built at version `1.4.14.0`; ZIP's 809 entries were read successfully and its `Hinge.dll` matched the Release output (`94D8B62C65B155F62836E42CC0B397C4F28354CDFD48C353AB03E10B2FD2C915`).
- Installer: 203,902,828 bytes, SHA-256 `F82246E62391DE352BD7EDFA6505F69D29B2BD64F85C1B76E1B2262F048968D6`; outer Authenticode status `NotSigned`.
- Portable ZIP: 135,519,011 bytes, SHA-256 `F425839E41C3B20E12A1C1C6BEBB795CBF36FB126A28155D67FC91C25DD8F6CE`.
- Android sources were unchanged. Mainline APK rebuilt and verified with v2/v3 signatures: `com.hinge.office` `1.4.14+119`, 21,159,073 bytes, SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`.
- Pre-change installer, ZIP, and APK are preserved in `publish/rollback/pre-input-text-vertical-alignment-20261011/packages/`.

## Not verified

The Hinge window was not available for a post-change screenshot, so vertical centering is supported by the shared WinUI control property and a successful compiled release, but was not visually inspected in the running application. The candidate package was not installed over `D:\Hinge`.

