# Keep project classes, receivers and activities
-keep class com.xdripwidget.android.** { *; }

# WorkManager
-keep class androidx.work.** { *; }
-keep class * extends androidx.work.Worker {
    public <init>(android.content.Context, androidx.work.WorkerParameters);
}
-keep class * extends androidx.work.ListenableWorker {
    public <init>(android.content.Context, androidx.work.WorkerParameters);
}

# AndroidX AppCompat & Core
-keep class androidx.appcompat.** { *; }
-keep class androidx.core.** { *; }

# Strip line numbers and debug info to minimize size
-renamesourcefileattribute SourceFile
-keepattributes SourceFile,LineNumberTable
