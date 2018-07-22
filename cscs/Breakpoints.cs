using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SplitAndMerge
{
    public class Breakpoints
    {
        static Breakpoints m_instance;
        Debugger m_debugger;

        Dictionary<string, HashSet<int>> m_breakpoints = new Dictionary<string, HashSet<int>>();

        public void SetDebugger(Debugger debugger)
        {
            m_debugger = debugger;
        }

        public static Breakpoints Instance
        {
            get
            {
                if (m_instance == null)
                {
                    m_instance = new Breakpoints();
                }
                return m_instance;
            }
        }

        public void AddBreakpoints(Debugger debugger, string data)
        {
            m_debugger = debugger;
            string[] parts = data.Split(new char[] { '|' });

            string filename = Path.GetFileName(parts[0]);

            HashSet<int> bps = new HashSet<int>();
            int lineNr;
            for (int i = 1; i < parts.Length; i++)
            {
                Int32.TryParse(parts[i], out lineNr);
                bps.Add(lineNr);
            }
            m_breakpoints[filename] = bps;
        }

        public void AddBreakpoint(string filename, int lineNr)
        {
            HashSet<int> bps;
            if (!m_breakpoints.TryGetValue(filename, out bps))
            {
                bps = new HashSet<int>();
            }
            bps.Add(lineNr);
            m_breakpoints[filename] = bps;
        }

        public void RemoveBreakpoint(string filename, int lineNr)
        {
            HashSet<int> bps;
            if (!m_breakpoints.TryGetValue(filename, out bps))
            {
                return;
            }
            bps.Remove(lineNr);
            //m_breakpoints[filename] = bps;
        }

        public bool BreakpointExists(ParsingScript script)
        {
            if (m_debugger == null || m_breakpoints.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(script.Filename))
            {
                return false;
            }

            string filename = Path.GetFileName(script.Filename);

            HashSet<int> bps;
            if (!m_breakpoints.TryGetValue(filename, out bps))
            {
                return false;
            }

            int line = script.GetOriginalLineNumber();
            return bps.Contains(line);
        }

    }
}
