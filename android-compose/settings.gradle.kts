pluginManagement {
    repositories { google(); mavenCentral(); gradlePluginPortal() }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        exclusiveContent { forRepository { maven("https://jitpack.io") }; filter { includeGroup("com.github.QWEA0") } }; google(); mavenCentral() }
}
rootProject.name = "HingeCompose"
include(":app")
