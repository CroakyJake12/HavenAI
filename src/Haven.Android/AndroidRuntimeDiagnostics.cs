using DApp=global::Android.App.Application;
using DActivity=global::Android.App.Activity;
using DDialog=global::Android.App.AlertDialog;
using DContext=global::Android.Content.Context;
using DIntent=global::Android.Content.Intent;
using DClip=global::Android.Content.ClipData;
using DClipboard=global::Android.Content.ClipboardManager;
using DBuild=global::Android.OS.Build;
using DEnvironment=global::Android.Runtime.AndroidEnvironment;
using DLog=global::Android.Util.Log;
using DToast=global::Android.Widget.Toast;
using DToastLength=global::Android.Widget.ToastLength;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Haven.Android;

internal static class AndroidRuntimeDiagnostics
{
    private const string Tag="Haven";
    private const int MaxLog=131_072;
    private static readonly object Gate=new();
    private static string? _path;
    private static WeakReference<DActivity>? _activity;
    private static int _initialized;
    private static int _presented;

    public static void Initialize(DApp app)
    {
        if(Interlocked.Exchange(ref _initialized,1)!=0)return;
        try
        {
            var dir=app.FilesDir?.AbsolutePath;
            if(!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
                _path=Path.Combine(dir,"haven-runtime-errors.log");
            }
        }
        catch(Exception e){DLog.Error(Tag,"Runtime-report setup failed: "+e.Message);}

        AppDomain.CurrentDomain.UnhandledException+=(_,a)=>Record(
            a.ExceptionObject as Exception??new InvalidOperationException("A non-Exception fatal error occurred."),
            "Unhandled managed exception",false);
        TaskScheduler.UnobservedTaskException+=(_,a)=>Record(a.Exception,"Unobserved task exception",true);
        DEnvironment.UnhandledExceptionRaiser+=(_,a)=>Record(a.Exception,"Unhandled Android runtime exception",false);
    }

    public static void Attach(DActivity activity)
    {
        _activity=new(activity);
        ShowPending(activity);
    }

    public static void Detach(DActivity activity)
    {
        if(_activity?.TryGetTarget(out var current)==true&&ReferenceEquals(current,activity))_activity=null;
    }

    public static void Record(Exception error,string context,bool showDialog)
    {
        var report=BuildReport(error,context);
        Save(report);
        DLog.Error(Tag,report);
        if(showDialog&&_activity?.TryGetTarget(out var activity)==true&&!activity.IsFinishing&&!activity.IsDestroyed)
        {
            Interlocked.Exchange(ref _presented,0);
            ShowPending(activity);
        }
    }

    public static void ShowStartupToast(DContext context)=>DToast.MakeText(
        context,"Haven could not start. A technical error report was saved.",DToastLength.Long)?.Show();

    private static void ShowPending(DActivity activity)
    {
        if(!TryRead(out var report)||Interlocked.CompareExchange(ref _presented,1,0)!=0)return;
        activity.RunOnUiThread(()=>
        {
            try
            {
                if(activity.IsFinishing||activity.IsDestroyed)
                {
                    Interlocked.Exchange(ref _presented,0);
                    return;
                }
                var builder=new DDialog.Builder(activity);
                builder.SetTitle("Haven encountered an error");
                builder.SetMessage(DialogMessage(report));
                builder.SetPositiveButton("Copy details",(_,_)=>Copy(activity,report));
                builder.SetNeutralButton("Share report",(_,_)=>Share(activity,report));
                builder.SetNegativeButton("Clear report",(_,_)=>Clear());
                var dialog=builder.Create();
                if(dialog is null)
                {
                    Interlocked.Exchange(ref _presented,0);
                    DLog.Error(Tag,"Runtime-error dialog creation failed.");
                    return;
                }
                dialog.Show();
            }
            catch(Exception e)
            {
                Interlocked.Exchange(ref _presented,0);
                DLog.Error(Tag,"Runtime-error dialog failed: "+e.Message);
            }
        });
    }

    private static string BuildReport(Exception error,string context)=>
        "Haven Android runtime report\nUTC: "+DateTimeOffset.UtcNow.ToString("O")
        +"\nContext: "+Sanitize(context)
        +"\nHaven version: "+(Assembly.GetExecutingAssembly().GetName().Version?.ToString()??"unknown")
        +"\nAndroid: "+(DBuild.VERSION.Release??"unknown")+" (API "+(int)DBuild.VERSION.SdkInt+")"
        +"\nDevice: "+Sanitize(DBuild.Manufacturer)+" "+Sanitize(DBuild.Model)
        +"\n\n"+Sanitize(error.ToString());

    private static string DialogMessage(string report)
    {
        var lines=report.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var context=lines.LastOrDefault(x=>x.StartsWith("Context:",StringComparison.Ordinal))??"Context: Runtime error";
        var message=lines.FirstOrDefault(x=>x.Contains("Exception:",StringComparison.Ordinal))
            ??lines.FirstOrDefault(x=>x.StartsWith("System.",StringComparison.Ordinal))
            ??"Technical details are available in the report.";
        return "Haven recorded a technical runtime error.\n\n"+context+"\n"+message
            +"\n\nCopy or share the report, then clear it after the problem has been recorded.";
    }

    private static string Sanitize(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "(not provided)";
        var r=Regex.Replace(value,@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+","Bearer [redacted]");
        r=Regex.Replace(r,@"(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret|authorization)\b\s*[:=]\s*[^\s,;]+","$1=[redacted]");
        r=Regex.Replace(r,@"/data/(?:user/\d+|data)/com\.cakemods\.haven","<app-data>");
        r=Regex.Replace(r,@"(?im)(?:[A-Z]:\\|/home/|/Users/)[^\r\n]*","<source-path>");
        return r.Length<=20_000?r:r[..20_000]+"\n[details truncated]";
    }

    private static void Save(string report)
    {
        lock(Gate)
        {
            if(string.IsNullOrWhiteSpace(_path))return;
            try
            {
                var old=File.Exists(_path)?File.ReadAllText(_path):string.Empty;
                var all=string.IsNullOrEmpty(old)?report:old+"\n\n----------------------------------------\n"+report;
                File.WriteAllText(_path,all.Length<=MaxLog?all:all[^MaxLog..]);
            }
            catch(Exception e){DLog.Error(Tag,"Runtime-report write failed: "+e.Message);}
        }
    }

    private static bool TryRead(out string report)
    {
        lock(Gate)
        {
            report=string.Empty;
            if(string.IsNullOrWhiteSpace(_path)||!File.Exists(_path))return false;
            try
            {
                report=File.ReadAllText(_path);
                return !string.IsNullOrWhiteSpace(report);
            }
            catch(Exception e)
            {
                DLog.Error(Tag,"Runtime-report read failed: "+e.Message);
                return false;
            }
        }
    }

    private static void Clear()
    {
        lock(Gate)
        {
            try{if(!string.IsNullOrWhiteSpace(_path))File.Delete(_path);}
            catch(Exception e){DLog.Error(Tag,"Runtime-report clear failed: "+e.Message);}
        }
    }

    private static void Copy(DActivity activity,string report)
    {
        try
        {
            if(activity.GetSystemService(DContext.ClipboardService) is not DClipboard clipboard)return;
            var clip=DClip.NewPlainText("Haven runtime report",report);
            if(clip is not null)clipboard.PrimaryClip=clip;
            DToast.MakeText(activity,"Haven error details copied.",DToastLength.Short)?.Show();
        }
        catch(Exception e){DLog.Error(Tag,"Runtime-report copy failed: "+e.Message);}
    }

    private static void Share(DActivity activity,string report)
    {
        try
        {
            var intent=new DIntent(DIntent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(DIntent.ExtraSubject,"Haven Android runtime report");
            intent.PutExtra(DIntent.ExtraText,report);
            var chooser=DIntent.CreateChooser(intent,"Share Haven error report");
            if(chooser is not null)activity.StartActivity(chooser);
        }
        catch(Exception e){DLog.Error(Tag,"Runtime-report share failed: "+e.Message);}
    }
}
