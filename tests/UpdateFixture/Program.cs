// Isolated updater test fixture. Never packaged as the real application.
string mode=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"mode.txt"));
if(mode=="fail")return 33;
int marker=Array.IndexOf(args,"--update-ready");
if(marker>=0&&marker+1<args.Length)File.WriteAllText(args[marker+1],"ready");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"fixture-launched.txt"),mode);
return 0;
