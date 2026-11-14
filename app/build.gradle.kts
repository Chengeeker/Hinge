plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}
android {
    testOptions { unitTests.isIncludeAndroidResources = true }
    namespace = "com.hinge.office"
    compileSdk { version = release(37) { minorApiLevel = 2 } }
    defaultConfig {
        applicationId = "com.hinge.office"
        minSdk = 24
        targetSdk = 36
        versionCode = 213
        versionName = "3.1.0"
        ndk { abiFilters += "arm64-v8a" }
    }
    buildFeatures { compose = true; buildConfig = true }
    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    buildTypes {
        release { isMinifyEnabled = false }
    }
    testOptions { unitTests.isReturnDefaultValues = true }
}
dependencies {
    testImplementation("org.robolectric:robolectric:4.16")
    testImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-test-manifest")

    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.10.0")
    implementation("io.github.kyant0:backdrop:1.0.6")
    implementation("com.github.QWEA0:liquidglass:v2.0.11")
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.5")
    implementation(platform("androidx.compose:compose-bom:2026.09.00"))
    implementation("androidx.activity:activity-compose:1.13.0")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.core:core-ktx:1.18.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
}


// A transitive runtime upgrade must not leave app bytecode compiled against an older Compose API.
val verifyComposeAlignment by tasks.registering {
    doLast {
        val report = mutableListOf<String>()
        val mismatches = mutableListOf<String>()
        for (variant in listOf("debug", "release")) {
            fun versions(suffix: String) = configurations.getByName(variant + suffix)
                .incoming.resolutionResult.allComponents.mapNotNull { it.moduleVersion }
                .filter { it.group.startsWith("androidx.compose.") }
                .associate { "${it.group}:${it.name}" to it.version }
            val compile = versions("CompileClasspath")
            val runtime = versions("RuntimeClasspath")
            for ((module, version) in compile.toSortedMap()) {
                val packaged = runtime[module]
                val line = "$variant $module compile=$version runtime=$packaged"
                report += line
                if (version != packaged) mismatches += line
            }
        }
        val output = layout.buildDirectory.file("reports/compose-alignment.txt").get().asFile
        output.parentFile.mkdirs()
        output.writeText(report.joinToString("\n"))
        check(mismatches.isEmpty()) { "Compose compile/runtime mismatch:\n" + mismatches.joinToString("\n") }
        logger.lifecycle("Compose compile/runtime alignment verified for debug and release")
    }
}
tasks.matching { it.name == "preBuild" }.configureEach { dependsOn(verifyComposeAlignment) }
