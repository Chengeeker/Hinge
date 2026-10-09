# app_process loads this entry point outside the app's Java call graph.
-keep class com.hinge.office.AdbClipboardBridge {
    public static void main(java.lang.String[]);
}
