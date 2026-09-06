using CSCS.InterpreterManager;
//using CSCS.Tests;
//using CSCSMath;
using SplitAndMerge;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSCS.ConsoleApp
{
    public class CscsConsoleApp
    {
        const string EXT = "cscs";

        enum NEXT_CMD
        {
            NONE = 0,
            PREV = -1,
            NEXT = 1,
            TAB = 2
        };

        protected InterpreterManagerModule _interpreterManager;
        protected Interpreter InterpreterInstance => _interpreterManager?.CurrentInterpreter;
        protected bool _startDebugger = true;

        protected virtual List<ICscsModule> GetModuleList()
        {
            return new List<ICscsModule>
            {
                new CscsSqlModule(),
                //new CscsMathModule(),
                _interpreterManager
            };
        }

        protected virtual void SetupMono()
        {
            Environment.SetEnvironmentVariable("MONO_REGISTRY_PATH",
                "/Library/Frameworks/Mono.framework/Versions/Current/etc/mono/registry/");
        }

        public int Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                Console.WriteLine();
                Console.WriteLine("Goodbye! ¡Adiós! Ciao! Adieu! Adeus! Tschüss! Пока! 再见 さようなら הֱיה שלום وداعا");
            };

            //Transform();
            //AddCol();
            //Extract2();
            //GetVerbs();
            //AddResources();

            ClearLine();

            SetupMono();

            // Second argument selects the debug server: "debugger" keeps it on, anything
            // else ("nodebugger") turns it off. Reading args[1] unconditionally threw
            // IndexOutOfRange for a single argument, so a plain "app script.cscs" crashed.
            if (_startDebugger)
            {
                _startDebugger = args.Length < 1 ||
                    (args.Length > 1 && args[1].Equals("debugger", StringComparison.OrdinalIgnoreCase));
            }

            // With no arguments at all the app is purely a debug server: the VS Code client
            // sends the file to debug, so running a script here as well would execute one
            // twice -- once now, and once when the client attaches.
            bool debugServerOnly = _startDebugger && args.Length == 0;

            _interpreterManager = new InterpreterManagerModule();
            _interpreterManager.Modules = GetModuleList();

            _interpreterManager.OnInterpreterCreated += InterpreterCreated;

            var interpreterId = _interpreterManager.NewInterpreter();
            _interpreterManager.SetInterpreter(interpreterId);
            if (_startDebugger)
            {
                var started = DebuggerServer.StartServer(DebuggerPort,
                    !string.IsNullOrWhiteSpace(DebuggerServer.AllowedClients));
                if (started != "OK")
                {
                    // Binding can fail (most often the port is still held by a previous
                    // run). Saying "listening" here would leave the user waiting forever
                    // for a client on a socket that was never opened.
                    Console.WriteLine();
                    Console.WriteLine("Debug server did NOT start: " + started);
                    return 1;
                }
            }

            int exitCode = 0;

            string scriptFilename = "../../../Scripts/Samples/bug.cscs";
            scriptFilename = "/Users/vass/GitHub/CSCS-web-1/wwwroot/scripts/article2/test3.cscs";
            string script = "";
            // Fetch the shared script from the server and run its self-tests. Non-fatal:
            // an unreachable server must never stop the local script from running.
            if (RunSharedScriptOnStartup && !debugServerOnly)
            {
                TestSharedScript(SharedScriptUrl);
            }

            if (debugServerOnly)
            {
                Console.WriteLine("CSCS debug server listening on port " + DebuggerPort +
                    ". Waiting for a client to attach (Ctrl+C to stop).");
                var stopRequested = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (sender, e) => { e.Cancel = true; stopRequested.Set(); };
                stopRequested.Wait();
                _interpreterManager.TerminateModules();
                return 0;
            }

            if (args.Length >= 3)
            {
                InterpreterInstance.Translation.TranslateScript(args);
                exitCode = InterpreterInstance.ExitCode;
            }
            else
            {
                if (args.Length > 0)
                {
                    if (args[0].EndsWith(EXT))
                    {
                        scriptFilename = args[0];
                        Console.WriteLine("Reading script from " + scriptFilename);
                        script = Utils.GetFileContents(scriptFilename);
                    }
                    else
                    {
                        script = args[0];
                    }
                }

                if (!string.IsNullOrWhiteSpace(script) || !string.IsNullOrWhiteSpace(scriptFilename))
                {
                    //ProcessScript(script, scriptFilename);
                    RunStartScript(scriptFilename);
                }

                exitCode = RunLoop();
            }

            _interpreterManager.TerminateModules();

            return exitCode;
        }

        /// <summary>Port the CSCS debug server listens on; must match "serverPort" in the
        /// VS Code launch configuration.</summary>
        protected virtual int DebuggerPort =>
            int.TryParse(Environment.GetEnvironmentVariable("CSCS_DEBUGGER_PORT"), out var port)
                ? port : 13337;

        /// <summary>Shared script published at &lt;webroot&gt;/shared/script.cscs.</summary>
        protected virtual string SharedScriptUrl =>
            Environment.GetEnvironmentVariable("CSCS_SHARED_SCRIPT_URL")
            ?? "http://185.32.124.162:17575/shared/script.cscs";

        /// <summary>
        /// Whether to fetch and execute the shared script at startup. This runs whatever
        /// the server returns, in this process -- only leave it on for a server you control.
        /// </summary>
        protected virtual bool RunSharedScriptOnStartup => true;

        /// <summary>
        /// Downloads the shared script and runs it. It reports its own checks and throws on
        /// the first failure, so completing without an exception means everything passed.
        /// </summary>
        protected bool TestSharedScript(string url)
        {
            Console.WriteLine("Fetching shared script: " + url);

            string localPath;
            try
            {
                localPath = SplitAndMerge.DownloadFileFunction.Download(url).AsString();
            }
            catch (Exception exc)
            {
                Console.WriteLine("  download FAILED: " + exc.Message);
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(localPath);
            }
            catch (Exception exc)
            {
                Console.WriteLine("  could not read " + localPath + ": " + exc.Message);
                return false;
            }

            if (text.TrimStart().StartsWith("<"))
            {
                // IIS serves an HTML error or directory page rather than the file when the
                // .cscs extension has no MIME mapping. Say so, instead of failing later with
                // a confusing parse error.
                Console.WriteLine("  FAILED: the server returned markup, not a script.");
                Console.WriteLine("  Add a MIME mapping for .cscs (see Scripts/Shared/web.config).");
                return false;
            }

            Console.WriteLine("  downloaded " + text.Length + " bytes to " + localPath);
            try
            {
                InterpreterInstance.Process(text, localPath, false);
                Console.WriteLine("Shared script passed.");
                return true;
            }
            catch (Exception exc)
            {
                Console.WriteLine("Shared script FAILED: " + exc.Message);
                return false;
            }
        }

        void GetVerbs()
        {
            List<string> verbs = new List<string>();
            var dict = "/Users/vass/GitHub/cscs_maui/Resources/Raw/dictionary.txt";
            var lines = File.ReadAllLines(dict);
            int count = 0;
            foreach(var line in lines)
            {
                var tokens = line.Split('\t');
                if (tokens.Length < 10 || tokens[0] != "verbs")
                {
                    continue;
                }
                var verbTok = tokens[8].Split(',');
                //if (++count >= 120)
                //    int lol = 0;
                foreach (var verb in verbTok)
                {
                    if (string.IsNullOrWhiteSpace(verb))
                    {
                        continue;
                    }
                    //var tok = verb.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    //var cand = tok[0].Trim().EndsWith("se") ? tok[0].Trim().Substring(0, tok[0].Trim().Length - 2) : tok[0];
                    var tok = verb.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    //var cand = tok[tok.Length - 1];
                    var cand = tok[0].Trim();

                    if (cand == "per" || cand == "se" || cand == "pour")
                    {
                        cand = tok[tok.Length - 1]; // IT, FR
                        if (cand == "debout")
                        {
                            cand = tok[tok.Length - 2]; // IT, FR
                        }
                    }
                    if (cand.EndsWith("-se"))
                    { // PT
                        cand = cand.Substring(0, cand.Length - 3);
                    }
                    if (!string.IsNullOrWhiteSpace(cand) && !verbs.Contains(cand))
                    {
                        verbs.Add(cand.Trim());
                    }
                }
                /*verbTok = tokens[7].Split(',');
                foreach (var verb in verbTok)
                {
                    if (string.IsNullOrWhiteSpace(verb))
                    {
                        continue;
                    }
                    var tok = verb.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var cand = tok[0].Trim().EndsWith("se") ? tok[0].Trim().Substring(0, tok[0].Trim().Length - 2) : tok[0];
                    if (!string.IsNullOrWhiteSpace(cand) && !verbs.Contains(cand))
                    {
                        verbs.Add(cand.Trim());
                    }
                }*/
            }
            var siteEn = "https://www.gymglish.com/en/conjugation/english/verb/";
            var siteEs = "https://www.gymglish.com/es/conjugacion/espanol/verbo/";
            var siteDe = "https://www.gymglish.com/de/konjugation/deutsch/verb/";
            var siteIt = "https://www.gymglish.com/es/conjugacion/italiano/verbo/";
            var siteFr = "https://www.gymglish.com/en/conjugation/french/verb/";
            //NO var sitePt = "https://www.collinsdictionary.com/conjugation/portuguese/";
            //NO var sitePt = "https://www.infopedia.pt/dicionarios/verbos-portugueses/";
            var sitePt = "https://www.conjugacao.com.br/verbo-";
            var siteRu = "https://www.translate.ru/%D1%81%D0%BF%D1%80%D1%8F%D0%B6%D0%B5%D0%BD%D0%B8%D0%B5%20%D0%B8%20%D1%81%D0%BA%D0%BB%D0%BE%D0%BD%D0%B5%D0%BD%D0%B8%D0%B5/%D1%80%D1%83%D1%81%D1%81%D0%BA%D0%B8%D0%B9/";
            //ProcessWebRequestEn("err", siteEn);
            //ProcessWebRequestEn("forget", siteEn);
            //ProcessWebRequestEn("shoot", siteEn);
            //ProcessWebRequestDe("irren", siteDe);
            //ProcessWebRequestDe("vergessen", siteDe);
            //ProcessWebRequestDe("schießen", siteDe);
            //ProcessWebRequestEs("errar", siteEs);
            //ProcessWebRequestEs("equivocarse", siteEs);
            //ProcessWebRequestEs("olvidar", siteEs);
            //ProcessWebRequestEs("disparar", siteEs);
            //ProcessWebRequestPt("errar", sitePt);
            //ProcessWebRequestPt("esquecer", sitePt);
            //ProcessWebRequestPt("disparar", sitePt);
            //ProcessWebRequestRu("ошибиться", siteRu);
            //ProcessWebRequestRu("забыть", siteRu);
            //ProcessWebRequestRu("стрелять", siteRu);
            ProcessWebRequestFr("se tromper", siteFr);
            ProcessWebRequestFr("oublier", siteFr);
            //ProcessWebRequestFr("se tromper", siteFr);
            //ProcessWebRequestFr("guérir", siteFr);
            ProcessWebRequestIt("sbagliare", siteIt);
            ProcessWebRequestIt("dimenticare", siteIt);
            ProcessWebRequestIt("sparare", siteIt);
            var site = sitePt;
            count = 0;
            foreach (var verb in verbs)
            {
                count++;
                //if (count >= 127) // 22, 143
                  ProcessWebRequestPt(verb, site);
            }
            Console.WriteLine(count);
        }
        string ProcessWebRequestDe(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            //var toks = verb.Split(' ');
            //uri += "to_" + toks[1];
            //string result = verb + "," + toks[1] + ",";
            uri += verb;
            //uri += ".html";
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }
            // to be, be, been, being, am, are, is, was, were, was
            // hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
            // hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
            // haría,harías,haría,haríamos,haríais,harían

            data = data.Replace("<span class=\"vert\">", "");
            //int ind = data.LastIndexOf("conjugation-info");
            //var info = Extract(data, ref ind, "conjugation-info", "</div>");
            //var items = info.Split(',');
            //result += items[2] + ", ";

            //int ind = data.IndexOf("Participo<");
            int ind = data.LastIndexOf("Perfekt<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";
            //ind = data.IndexOf("Gerundio<");
            ind = data.LastIndexOf("Präsens<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            //ind = data.IndexOf("Presente<");
            ind = data.IndexOf("Präsens<");
            var items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            //ind = data.IndexOf("Pretérito imperfecto<");
            ind = data.IndexOf("Perfekt<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Plusquamperfekt<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Präteritum<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("I Präsens<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("II Präteritum<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";
            
            //ind = data.IndexOf("Pretérito indefinido<");
            ind = data.IndexOf("Imperativ<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            //result += (items.Length < 2 ? items[0] : items[1]) + ",";
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1];

            /*ind = data.IndexOf("Futuro<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Condicional<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1]; */
            /*ind = data.IndexOf("Present progressive");
            result += Extract(data, ref ind) + ", ";

            ind = data.IndexOf("Present (simple)");
            result += Extract(data, ref ind) + ",";
            result += Extract(data, ref ind) + ",";
            result += Extract(data, ref ind) + ", ";

            ind = data.IndexOf("Past (simple)");
            result += Extract(data, ref ind) + ",";
            result += Extract(data, ref ind) + ",";
            result += Extract(data, ref ind);

            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/en_verbs.txt";*/
            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/de_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestEs(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            //var toks = verb.Split(' ');
            //uri += "to_" + toks[1];
            //string result = verb + "," + toks[1] + ",";
            uri += verb;
            //uri += ".html";
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }
            // to be, be, been, being, am, are, is, was, were, was
            // hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
            // hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
            // haría,harías,haría,haríamos,haríais,harían

            data = data.Replace("<span class=\"vert\">", "");
            //int ind = data.LastIndexOf("conjugation-info");
            //var info = Extract(data, ref ind, "conjugation-info", "</div>");
            //var items = info.Split(',');
            //result += items[2] + ", ";

            int ind = data.IndexOf("Participo<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";
            ind = data.IndexOf("Gerundio<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Presente<");
            var items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Pretérito imperfecto<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            /* hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían, 
             * hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán, 
             * haría,harías,haría,haríamos,haríais,harían
             * 
               hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
              hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
              haría,harías,haría,haríamos,haríais,harían, 
 */
            ind = data.IndexOf("Pretérito indefinido<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Futuro<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Condicional<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1];
            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/es_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestIt(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            uri += verb;
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }
            // to be, be, been, being, am, are, is, was, were, was
            // hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
            // hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
            // haría,harías,haría,haríamos,haríais,harían

            data = data.Replace("<span class=\"vert\">", "");
            //int ind = data.LastIndexOf("conjugation-info");
            //var info = Extract(data, ref ind, "conjugation-info", "</div>");
            //var items = info.Split(',');
            //result += items[2] + ", ";

            int ind = data.IndexOf("Participio<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";
            ind = data.IndexOf("Gerundio<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Presente<");
            var items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Passato prossimo<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            /* hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían, 
             * hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán, 
             * haría,harías,haría,haríamos,haríais,harían
             * 
               hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
              hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
              haría,harías,haría,haríamos,haríais,harían, 
 */
            ind = data.IndexOf("Imperfetto<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Futuro<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Passato remoto<");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            var ind0 = data.IndexOf("Condizionale<");
            ind = data.IndexOf("Presente<", ind0);
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind0 = data.IndexOf("Congiuntivo<");
            ind = data.IndexOf("Presente<", ind0);
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind = data.IndexOf("Imperfetto<", ind0);
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ", ";

            ind0 = data.IndexOf("Imperativo<");
            ind = data.IndexOf("Presente<", ind0);
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1] + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1];
            //items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            //result += items[1];

            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/it_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestFr(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            uri += verb;
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }

            data = data.Replace("<span class=\"vert\">", "");
            int ind = data.IndexOf("Participe<");
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";
            ind = data.IndexOf("Passé<", ind);
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Présent<");
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Passé composé<");
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Imparfait<");
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Passé simple<");
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Futur<");
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            var ind0 = data.IndexOf("Conditionnel<");
            ind = data.IndexOf("Présent<", ind0);
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind0 = data.IndexOf("Subjonctif<");
            ind = data.IndexOf("Présent<", ind0);
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind = data.IndexOf("Imparfait<", ind0);
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ", ";

            ind0 = data.IndexOf("Impératif<");
            ind = data.IndexOf("Présent<", ind0);
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>") + ",";
            result += Extract(data, ref ind, "<li>", "</span>");

            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/fr_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestEn(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            //var toks = verb.Split(' ');
            //uri += "to_" + toks[1];
            //string result = verb + "," + toks[1] + ",";
            uri += verb;
            //uri += ".html";
            string result = "to " +  verb + ", " + verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }
            // to be, be, been, being, am, are, is, was, were, was
            // hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
            // hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
            // haría,harías,haría,haríamos,haríais,harían

            data = data.Replace("<span class=\"vert\">", "");
            int ind = data.LastIndexOf("conjugation-info");
            var info = Extract(data, ref ind, "conjugation-info", "</div>");
            var items = info.Split(',');
            result += items[2] + ", ";

            //int ind = data.IndexOf("Participo<");
            ind = data.LastIndexOf(">Present progressive");
            result += Extract(data, ref ind, "<li>", "</span>").Split(' ')[2] + ", ";
            //ind = data.IndexOf("Gerundio<");

            ind = data.IndexOf("Present (simple)");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim() + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim() + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim() + ", ";
            ind = data.IndexOf("Past (simple)");
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim() + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim() + ",";
            items = Extract(data, ref ind, "<li>", "</span>").Split(new char[] { ' ' }, 2);
            result += items[1].Trim();

            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/en_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestPt(string verb, string uri, string method = "GET", string load = "",
            string contentType = "application/x-www-form-urlencoded")
        {
            uri += verb;
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }

            data = data.Replace("<span class=\"vert\">", "");
            data = data.Replace("<span class=\"f irregular\">", "<span class=\"f\">");
            int ind = data.IndexOf("Gerúndio<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            ind = data.IndexOf("Particípio passado<", ind);
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Presente<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Pretérito Imperfeito<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Pretérito Perfeito<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Pretérito Mais-que-perfeito<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Futuro do Presente<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Futuro do Pretérito<");
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            var ind0 = data.IndexOf("Subjuntivo<");
            ind = data.IndexOf("Presente<", ind0);
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind0 = data.IndexOf("Subjuntivo<");
            ind = data.IndexOf("Futuro<", ind0);
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ", ";

            ind = data.IndexOf("Imperativo<", ind0);
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + ",";
            result += Extract(data, ref ind, "<span class=\"f\">", "</span>") + "";

            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/pt_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }
        string ProcessWebRequestRu(string verb, string uri, string method = "GET", string load = "",
    string contentType = "application/x-www-form-urlencoded")
        {
            //var toks = verb.Split(' ');
            //uri += "to_" + toks[1];
            //string result = verb + "," + toks[1] + ",";
            uri += verb;
            //uri += ".html";
            string result = verb + ", ";
            string data = "";
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    data = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
            }
            catch (Exception exc)
            {
                result = exc.Message;
            }
            // to be, be, been, being, am, are, is, was, were, was
            // hacer, hecho, haciendo, hago,haces,hace,hacemos,hacéis,hacen, hacía,hacías,hacía,hacíamos,hacíais,hacían,
            // hice,hiciste,hizo,hicimos,hicisteis,hicieron, haré,harás,hará,haremos,haréis,harán,
            // haría,harías,haría,haríamos,haríais,harían

            data = data.Replace("<f>", ""); data = data.Replace("</f>", "");
            data = data.Replace("<span class=\"vert\">", "");
            //int ind = data.LastIndexOf("conjugation-info");
            //var info = Extract(data, ref ind, "conjugation-info", "</div>");
            //var items = info.Split(',');
            //result += items[2] + ", ";

            int ind = data.IndexOf("class=\"hdr\">причастие");
            result += Extract(data, ref ind, "<value>", "</value>") + ", ";
            ind = data.IndexOf("class=\"hdr\">деепричастие");
            result += Extract(data, ref ind, "<value>", "</value>") + ", ";

            ind = data.IndexOf("настоящее время</b>", StringComparison.OrdinalIgnoreCase);
            var items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2);
            try
            {
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ", ";
            }
            catch (Exception exc) { }

            ind = data.IndexOf("прошедшее время</b>", StringComparison.OrdinalIgnoreCase);
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1].Replace(" ", "") + ", ";

            ind = data.IndexOf("будущее время</b>", StringComparison.OrdinalIgnoreCase);
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            try
            {
                result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ", ";
            }
            catch (Exception exc) { }

            ind = data.IndexOf(">сослагательное наклонение</h3>", StringComparison.OrdinalIgnoreCase);
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[1] + ", ";

            ind = data.IndexOf(">повелительное наклонение</h3>", StringComparison.OrdinalIgnoreCase);
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[0] + ",";
            items = Extract(data, ref ind, "<value>", "</value>").Split(new char[] { ' ' }, 2); ;
            result += items[0];
            //ind = data.IndexOf("Presente<");
            string path = @"/Users/vass/GitHub/cscs_maui/Resources/Raw/ru_verbs.txt";
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(result);
            }
            return result;
        }

        string Extract(string data, ref int from, string s1 = "<span class=\"vert\">", string s2= "</span>")
        {
            string result = "";
            if (from < 0)
                return result;
            var ind1 = data.IndexOf(s1, from);
            if (ind1 < 0)
            {
                return result;
            }
            var ind2 = data.IndexOf(s2, ind1 + s1.Length);
            if (ind2 < 0)
            {
                return result;
            }
            result = data.Substring(ind1 + s1.Length, ind2 - ind1 - s1.Length).Trim();
            from = ind2 + s2.Length;

            return result;
        }
        void AddResources(string filename = "/Users/vass/Downloads/trans.txt",
            string keys = "/Users/vass/Downloads/text1.txt",
            string results = "/Users/vass/Downloads/AppResources.he.resx")
        {
            var lines = File.ReadAllLines(filename);
            var lines1 = File.ReadAllLines(keys);
            List<string> output = new List<string>();
            int i = 0;
            foreach (var line in lines)
            {
                var word = line.Trim();
                var word1 = lines1[i++];
                var newLine = string.Format("<data name={0} xml:space=\"preserve\">\n" +
                    "  <value>{1}</value>\n</data>", word1, word);
                output.Add(newLine);
            }
            File.WriteAllLines(results, output.ToArray());
            Console.WriteLine("Wrote {0} lines to {1}", output.Count, results);
        }
        void Extract(string filename = "/Users/vass/Downloads/AppResources.resx",
            string results = "/Users/vass/Downloads/text.txt")
        {
            var data = File.ReadAllText(filename);
            List<string> output = new List<string>();
            int from = 0;
            while (true)
            {
                from = data.IndexOf("<data name=", from);
                if (from < 0)
                {
                    break;
                }
                from = data.IndexOf("<value>", from);
                if (from < 0)
                {
                    break;
                }
                from += 7;
                int to = data.IndexOf("</value>", from);
                if (to < 0)
                {
                    break;
                }
                var trans = data.Substring(from, to - from);
                output.Add(trans);
                from = to;
            }
            File.WriteAllLines(results, output.ToArray());
            Console.WriteLine("Wrote {0} lines to {1}", output.Count, results);
        }
        void Extract2(string filename = "/Users/vass/Downloads/AppResources.resx",
            string results = "/Users/vass/Downloads/text1.txt")
        {
            var data = File.ReadAllText(filename);
            List<string> output = new List<string>();
            int from = 0;
            while (true)
            {
                from = data.IndexOf("<data name=", from);
                if (from < 0)
                {
                    break;
                }
                int to = data.IndexOf(" xml:space=", from);
                if (to < 0)
                {
                    break;
                }
                from += 11;
                var key = data.Substring(from, to - from);
                output.Add(key);
                from = to;
            }
            File.WriteAllLines(results, output.ToArray());
            Console.WriteLine("Wrote {0} lines to {1}", output.Count, results);
        }
        void AddCol(string filename = "/Users/vass/Downloads/he.csv",
            string file2 = "/Users/vass/Downloads/dictionary.txt",
            string results = "/Users/vass/Downloads/all.txt")
        {
            var linesCol = File.ReadAllLines(filename);
            var lines = File.ReadAllLines(file2);
            List<string> output = new List<string>();
            int ln = 0;
            foreach (var line in lines)
            {
                string newLine = string.Empty;
                var parts = line.Split('\t');
                var line2 = linesCol[ln++].Trim().TrimStart('"').TrimEnd('"').Trim().TrimStart('"').TrimEnd('"').Trim();
                for (int i = 0; i < 11; i++)
                {
                    newLine += parts[i] + '\t';
                }
                newLine += line2 + '\t';
                for (int i = 11; i < parts.Length; i++)
                {
                    newLine += parts[i] + '\t';
                }
                output.Add(newLine.Trim());
            }
            File.WriteAllLines(results, output.ToArray());
            Console.WriteLine("Wrote {0} lines to {1}", output.Count, results);

        }
        void Transform(string filename = "zh-Hans.lproj/Localizable.strings", string file2 = "AppResources.en-US.resx")
        {
            ///Users/vass/GitHub/mobile/iOS/Resources/en.lproj/Localizable.strings
            HashSet<string> cache = new HashSet<string>();
            var dir = "/Users/vass/GitHub/mobile/iOS/Resources/";
            Directory.SetCurrentDirectory(dir);
            var src = System.IO.Path.Combine(dir, filename);
            var dst = System.IO.Path.Combine(dir, file2);
            var lines = File.ReadAllLines(src);
            List<string> output = new List<string>();
            foreach (var line in lines)
            {
                var parts = line.Split('=');
                if (parts.Length < 2)
                {
                    continue;
                }
                var word1 = parts[0].Trim();
                var word2 = parts[1].Trim();
                if (word1.Length < 3 || !word1.StartsWith("\"") || !word1.EndsWith("\"") ||
                    word2.Length < 4 || !word2.StartsWith("\"") || !word2.EndsWith(";"))
                {
                    continue;
                }
                word1 = word1.Replace("%@", "{0}").Replace("%d", "{0}").Replace("&", "and");
                word2 = word2.Replace("%@", "{0}").Replace("%d", "{0}").TrimStart('\"').TrimEnd(';').Trim().TrimEnd('\"');

                if (cache.Contains(word1))
                {
                    Console.WriteLine("Duplicate " + word1);
                    continue;
                }
                cache.Add(word1);

                var newLine = string.Format("<data name={0} xml:space=\"preserve\">\n" +
                    "  <value>{1}</value>\n</data>", word1, word2);
                output.Add(newLine);
            }
            File.WriteAllLines(dst, output.ToArray());
            Console.WriteLine("Wrote {0} lines to {1}", output.Count, dst);
        }

        private void InterpreterCreated(object sender, EventArgs e)
        {
            // Subscribe to the printing events from the interpreter.
            // A printing event will be triggered after each successful statement
            // execution. On error an exception will be thrown.
            if (sender is Interpreter interpreter)
            {
                interpreter.OnOutput += Print;
            }
        }

        private static void SplitByLast(string str, string sep, ref string a, ref string b)
        {
            int it = str.LastIndexOfAny(sep.ToCharArray());
            a = it == -1 ? "" : str.Substring(0, it + 1);
            b = it == -1 ? str : str.Substring(it + 1);
        }

        private static string CompleteTab(string script, string init, ref int tabFileIndex,
            ref string start, ref string baseStr, ref string startsWith)
        {
            if (tabFileIndex > 0 && !script.Equals(init))
            {
                // The user has changed something in the input field
                tabFileIndex = 0;
            }
            if (tabFileIndex == 0 || script.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                // The user pressed tab the first time or pressed it on a directory
                string path = "";
                SplitByLast(script, " ", ref start, ref path);
                SplitByLast(path, "/\\", ref baseStr, ref startsWith);
            }

            tabFileIndex++;
            string result = Utils.GetFileEntry(baseStr, tabFileIndex, startsWith);
            result = result.Length == 0 ? startsWith : result;
            return start + baseStr + result;
        }

        private int RunLoop()
        {
            List<string> commands = new List<string>();
            StringBuilder sb = new StringBuilder();
            int cmdPtr = 0;
            int tabFileIndex = 0;
            bool arrowMode = false;
            string start = "", baseCmd = "", startsWith = "", init = "", script;
            string previous = "";
            string prompt = GetPrompt();

            while (true)
            {
                if (!InterpreterInstance.IsRunning)
                {
                    int exitCode = InterpreterInstance.ExitCode;
                    _interpreterManager.SwitchFromAndRemoveInterpreter(InterpreterInstance);
                    if (InterpreterInstance == null)
                        return exitCode;
                }

                sb.Clear();

                NEXT_CMD nextCmd = NEXT_CMD.NONE;
                script = previous + GetConsoleLine(ref nextCmd, prompt, init).Trim();

                if (script.EndsWith(Constants.CONTINUE_LINE.ToString()))
                {
                    previous = script.Remove(script.Length - 1);
                    init = "";
                    prompt = "";
                    continue;
                }

                prompt = GetPrompt();

                if (nextCmd == NEXT_CMD.PREV || nextCmd == NEXT_CMD.NEXT)
                {
                    if (arrowMode || nextCmd == NEXT_CMD.NEXT)
                    {
                        cmdPtr += (int)nextCmd;
                    }
                    cmdPtr = cmdPtr < 0 || commands.Count == 0 ?
                                cmdPtr + commands.Count :
                                cmdPtr % commands.Count;
                    init = commands.Count == 0 ? script : commands[cmdPtr];
                    arrowMode = true;
                    continue;
                }
                else if (nextCmd == NEXT_CMD.TAB)
                {
                    init = CompleteTab(script, init, ref tabFileIndex,
                                ref start, ref baseCmd, ref startsWith);
                    continue;
                }

                init = "";
                previous = "";
                tabFileIndex = 0;
                arrowMode = false;

                if (string.IsNullOrWhiteSpace(script))
                {
                    continue;
                }

                if (commands.Count == 0 || !commands[commands.Count - 1].Equals(script))
                {
                    commands.Add(script);
                }
                if (!script.EndsWith(Constants.END_STATEMENT.ToString()))
                {
                    script += Constants.END_STATEMENT;
                }

                ProcessScript(script);
                cmdPtr = commands.Count - 1;
            }
        }

        static string GetConsoleLine(ref NEXT_CMD cmd, string prompt, string init = "",
                                                bool enhancedMode = true)
        {
            //string line = init;
            StringBuilder sb = new StringBuilder(init);
            int delta = init.Length - 1;
            Console.Write(prompt);
            Console.Write(init);

            if (!enhancedMode)
            {
                return Console.ReadLine() ?? String.Empty;
            }

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.UpArrow)
                {
                    cmd = NEXT_CMD.PREV;
                    ClearLine(prompt, sb.ToString());
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.DownArrow)
                {
                    cmd = NEXT_CMD.NEXT;
                    ClearLine(prompt, sb.ToString());
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.RightArrow)
                {
                    delta = Math.Max(-1, Math.Min(++delta, sb.Length - 1));
                    SetCursor(prompt, sb.ToString(), delta + 1);
                    continue;
                }
                if (key.Key == ConsoleKey.LeftArrow)
                {
                    delta = Math.Max(-1, Math.Min(--delta, sb.Length - 1));
                    SetCursor(prompt, sb.ToString(), delta + 1);
                    continue;
                }
                if (key.Key == ConsoleKey.Tab)
                {
                    cmd = NEXT_CMD.TAB;
                    ClearLine(prompt, sb.ToString());
                    return sb.ToString();
                }
                if (key.Key == ConsoleKey.Backspace || key.Key == ConsoleKey.Delete)
                {
                    if (sb.Length > 0)
                    {
                        delta = key.Key == ConsoleKey.Backspace ?
                            Math.Max(-1, Math.Min(--delta, sb.Length - 2)) : delta;
                        if (delta < sb.Length - 1)
                        {
                            sb.Remove(delta + 1, 1);
                        }
                        SetCursor(prompt, sb.ToString(), Math.Max(0, delta + 1));
                    }
                    continue;
                }
                if (key.Key == ConsoleKey.Enter)
                {
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        sb.Append(Constants.CONTINUE_LINE);
                    Console.WriteLine();
                    return sb.ToString();
                }
                if (key.KeyChar == Constants.EMPTY)
                {
                    continue;
                }

                ++delta;
                Console.Write(key.KeyChar);
                if (delta < sb.Length)
                {
                    delta = Math.Max(0, Math.Min(delta, sb.Length - 1));
                    sb.Insert(delta, key.KeyChar.ToString());
                }
                else
                {
                    sb.Append(key.KeyChar);
                }
                SetCursor(prompt, sb.ToString(), delta + 1);
            }
        }

        public static HashSet<string> GetPreprocessTokens()
        {
            var tokenSet = new HashSet<string>();
            var tokensStr = "include,startdebugger,return,function,cfunction,csfunction,add_comp_namespace,add_comp_definition,dllfunction,dllsub,importdll";
            if (string.IsNullOrWhiteSpace(tokensStr))
            {
                return tokenSet;
            }

            var tokens = tokensStr.Split(',');
            foreach (var token in tokens)
            {
                tokenSet.Add(token);
            }
            return tokenSet;
        }
        public  Variable RunStartScript(string fileName, bool encode = false)
        {
            //Init();

            if (encode)
            {
                //EncodeFileFunction.EncodeDecode(fileName, false);
            }
            string script = Utils.GetFileContents(fileName);
            if (encode)
            {
                //EncodeFileFunction.EncodeDecode(fileName, true);
            }

            //zakomentirat    (?)
            //PreprocessScripts();

            //preprocess this file
            var tokenSet = GetPreprocessTokens();
            var scriptsDirStr = "/Users/vass/GitHub/CSCS-web-1/wwwroot/scripts/";
            Utils.PreprocessScriptFile(fileName, tokenSet, scriptsDirStr);

            Variable result = null;
            try
            {
                result = InterpreterInstance.Process(script, fileName, true);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception: " + exc.Message);
                Console.WriteLine(exc.StackTrace);
                InterpreterInstance.InvalidateStacksAfterLevel(0);
                var onException = CustomFunction.Run(InterpreterInstance, Constants.ON_EXCEPTION, new Variable("Global Scope"),
                            new Variable(exc.Message), Variable.EmptyInstance);
                if (onException == null)
                {
                    throw;
                }
            }

            return result;
        }

        private void ProcessScript(string script, string filename = "")
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                script = Utils.GetFileContents(filename);
            }
            string data = Utils.ConvertToScript(InterpreterInstance, script, out Dictionary<int, int> char2Line, filename);
            ParsingScript toParse = new ParsingScript(InterpreterInstance, data, 0, char2Line);
            toParse.OriginalScript = script;
            toParse.Filename = filename;

            var dryRun = toParse.DryRun();

            s_PrintingCompleted = false;
            string errorMsg = null;
            Variable result = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    result = Task.Run(() =>
                    //Interpreter.Instance.ProcessFileAsync(filename, true)).Result;
                    InterpreterInstance.ProcessFile(filename, true)).Result;
                }
                else
                {
                    result = Task.Run(() =>
                        //Interpreter.Instance.ProcessAsync(script, filename)).Result;
                        InterpreterInstance.Process(script, filename, true)).Result;
                }
            }
            catch (Exception exc)
            {
                errorMsg = exc.InnerException != null ? exc.InnerException.Message : exc.Message;
                InterpreterInstance.InvalidateStacksAfterLevel(0);
            }

            if (!s_PrintingCompleted)
            {
                string output = InterpreterInstance.Output;
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output);
                }
                else if (result != null)
                {
                    output = result.AsString(false, false);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Console.WriteLine(output);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(errorMsg))
            {
                Utils.PrintColor(InterpreterInstance, errorMsg + Environment.NewLine, ConsoleColor.Red);
                errorMsg = string.Empty;
            }
        }

        protected virtual string GetPrompt()
        {
            const int MAX_SIZE = 30;
            string path = Directory.GetCurrentDirectory();
            if (path.Length > MAX_SIZE)
            {
                path = "..." + path.Substring(path.Length - MAX_SIZE);
            }

            return string.Format("{0}>>", path);
        }

        private static void ClearLine(string part1 = "", string part2 = "")
        {
            string spaces = new string(' ', part1.Length + part2.Length + 1);
            Console.Write("\r{0}\r", spaces);
        }

        private static void SetCursor(string prompt, string line, int pos)
        {
            ClearLine(prompt, line);
            Console.Write("{0}{1}\r{2}{3}",
                prompt, line, prompt, line.Substring(0, pos));
        }

        void Print(object sender, OutputAvailableEventArgs e)
        {
            Console.Write(e.Output);
            s_PrintingCompleted = true;
        }
        bool s_PrintingCompleted = false;
    }
}
