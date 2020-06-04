using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Verse;

namespace DoorsExpanded
{
    // Debug logging that includes deduped stack traces (what the T stands for). Enabled only if Prefs.LogVerbose is true.
    static class TLog
    {
        private static readonly ConcurrentDictionary<string, int> methodToCounter = new ConcurrentDictionary<string, int>();
        private static readonly ConcurrentDictionary<string, string> stackTraceToId = new ConcurrentDictionary<string, string>();

        public static bool Enabled => Prefs.LogVerbose;

        public static void Log(object obj)
        {
            if (!Enabled)
                return;
            var stackTrace = new StackTrace(1);
            var stackTraceStr = stackTrace.ToString().Replace("\r", "");
            var newId = false;
            var id = stackTraceToId.GetOrAdd(stackTraceStr, _ =>
            {
                newId = true;
                var sb = new StringBuilder();
                var method = stackTrace.GetFrame(0).GetMethod();
                var methodName = method.Name;
                if (method.DeclaringType is Type declaringType)
                    methodName = declaringType + "." + methodName;
                var counter = methodToCounter.AddOrUpdate(methodName,
                    addValueFactory: _ => 0,
                    updateValueFactory: (_, counter) => counter + 1);
                return $"{methodName} {{{counter}}}";
            });
            var msg = $"{obj} called from {id}";
            if (newId)
                msg += "\n" + stackTraceStr;
            UnityEngine.Debug.Log(msg);
        }
    }
}
