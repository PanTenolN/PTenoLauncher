using PTenoLauncher.Infrastructure.Updates;

// No arbitrary command-line transaction path, target directory, key or trust override is accepted.
if (args.Length != 0) return 2;
return LauncherUpdateRuntime.RunHelper();
