using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using SplitAndMerge;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if __ANDROID__
using scripting.Droid;
#elif __IOS__
using scripting.iOS;
#endif

namespace scripting
{
    public class CommonFunctions
    {
        public static void RegisterFunctions()
        {
            ParserFunction.RegisterFunction("GetLocation", new GetLocationFunction());
            ParserFunction.RegisterFunction("AddWidget", new AddWidgetFunction());
            ParserFunction.RegisterFunction("AddView", new AddWidgetFunction("View"));
            ParserFunction.RegisterFunction("AddButton", new AddWidgetFunction("Button"));
            ParserFunction.RegisterFunction("AddLabel", new AddWidgetFunction("Label"));
            ParserFunction.RegisterFunction("AddTextEdit", new AddWidgetFunction("TextEdit"));
            ParserFunction.RegisterFunction("AddTextView", new AddWidgetFunction("TextView"));
            ParserFunction.RegisterFunction("AddTextEditView", new AddWidgetFunction("TextEditView"));
            ParserFunction.RegisterFunction("AddImageView", new AddWidgetFunction("ImageView"));
            ParserFunction.RegisterFunction("AddPickerView", new AddWidgetFunction("Picker"));
            ParserFunction.RegisterFunction("AddTypePickerView", new AddWidgetFunction("TypePicker"));
            ParserFunction.RegisterFunction("AddSwitch", new AddWidgetFunction("Switch"));
            ParserFunction.RegisterFunction("AddSlider", new AddWidgetFunction("Slider"));
            ParserFunction.RegisterFunction("AddStepper", new AddWidgetFunction("Stepper"));
            ParserFunction.RegisterFunction("AddStepperLeft", new AddWidgetFunction("Stepper", "left"));
            ParserFunction.RegisterFunction("AddStepperRight", new AddWidgetFunction("Stepper", "right"));
            ParserFunction.RegisterFunction("AddListView", new AddWidgetFunction("ListView"));
            ParserFunction.RegisterFunction("AddCombobox", new AddWidgetFunction("Combobox"));
            ParserFunction.RegisterFunction("AddIndicator", new AddWidgetFunction("Indicator"));
            ParserFunction.RegisterFunction("AddSegmentedControl", new AddWidgetFunction("SegmentedControl"));

            ParserFunction.RegisterFunction("AddWidgetData", new AddWidgetDataFunction());
            ParserFunction.RegisterFunction("AddWidgetImages", new AddWidgetImagesFunction());
            ParserFunction.RegisterFunction("AddTab", new AddTabFunction(true));
            ParserFunction.RegisterFunction("AddOrSelectTab", new AddTabFunction(false));
            ParserFunction.RegisterFunction("GetSelectedTab", new GetSelectedTabFunction());
            ParserFunction.RegisterFunction("SelectTab", new SelectTabFunction());
            ParserFunction.RegisterFunction("OnTabSelected", new OnTabSelectedFunction());
            ParserFunction.RegisterFunction("AddBorder", new AddBorderFunction());
            ParserFunction.RegisterFunction("AutoScale", new AutoScaleFunction());
            ParserFunction.RegisterFunction("SetBaseWidth", new SetBaseWidthFunction());
            ParserFunction.RegisterFunction("AddLongClick", new AddLongClickFunction());
            ParserFunction.RegisterFunction("AddSwipe", new AddSwipeFunction());
            ParserFunction.RegisterFunction("AddDragAndDrop", new AddDragAndDropFunction());
            ParserFunction.RegisterFunction("ShowView", new ShowHideFunction(true));
            ParserFunction.RegisterFunction("HideView", new ShowHideFunction(false));
            ParserFunction.RegisterFunction("SetVisible", new ShowHideFunction(true));
            ParserFunction.RegisterFunction("RemoveView", new RemoveViewFunction());
            ParserFunction.RegisterFunction("RemoveAllViews", new RemoveAllViewsFunction());
            ParserFunction.RegisterFunction("RemoveTabViews", new RemoveAllViewsFunction());
            ParserFunction.RegisterFunction("GetX", new GetCoordinateFunction(true));
            ParserFunction.RegisterFunction("GetY", new GetCoordinateFunction(false));
            ParserFunction.RegisterFunction("MoveView", new MoveViewFunction(false));
            ParserFunction.RegisterFunction("MoveViewTo", new MoveViewFunction(true));
            ParserFunction.RegisterFunction("SetBackgroundColor", new SetBackgroundColorFunction());
            ParserFunction.RegisterFunction("SetBackground", new SetBackgroundImageFunction());
            ParserFunction.RegisterFunction("AddText", new AddTextFunction());
            ParserFunction.RegisterFunction("SetText", new SetTextFunction());
            ParserFunction.RegisterFunction("GetText", new GetTextFunction());
            ParserFunction.RegisterFunction("SetValue", new SetValueFunction());
            ParserFunction.RegisterFunction("GetValue", new GetValueFunction());
            ParserFunction.RegisterFunction("SetImage", new SetImageFunction());
            ParserFunction.RegisterFunction("SetFontColor", new SetFontColorFunction());
            ParserFunction.RegisterFunction("SetFontSize", new SetFontSizeFunction());
            ParserFunction.RegisterFunction("SetFont", new SetFontFunction());
            ParserFunction.RegisterFunction("SetBold", new SetFontTypeFunction(SetFontTypeFunction.FontType.BOLD));
            ParserFunction.RegisterFunction("SetItalic", new SetFontTypeFunction(SetFontTypeFunction.FontType.ITALIC));
            ParserFunction.RegisterFunction("SetNormalFont", new SetFontTypeFunction(SetFontTypeFunction.FontType.NORMAL));
            ParserFunction.RegisterFunction("AlignText", new AlignTitleFunction());
            ParserFunction.RegisterFunction("SetSize", new SetSizeFunction());
            ParserFunction.RegisterFunction("Enable", new EnableFunction());
            ParserFunction.RegisterFunction("Relative", new RelativeSizeFunction());
            ParserFunction.RegisterFunction("ShowHideKeyboard", new ShowHideKeyboardFunction());
            ParserFunction.RegisterFunction("IsKeyboard", new IsKeyboardFunction());
            ParserFunction.RegisterFunction("SetSecure", new MakeSecureFunction());

            ParserFunction.RegisterFunction("AddAction", new AddActionFunction());
            ParserFunction.RegisterFunction("AllowedOrientation", new AllowedOrientationFunction());
            ParserFunction.RegisterFunction("OnOrientationChange", new OrientationChangeFunction());
            ParserFunction.RegisterFunction("RegisterOrientationChange", new RegisterOrientationChangeFunction());
            ParserFunction.RegisterFunction("OnEnterBackground", new OnEnterBackgroundFunction());
            ParserFunction.RegisterFunction("KillMe", new KillMeFunction());
            ParserFunction.RegisterFunction("ShowToast", new ShowToastFunction());
            ParserFunction.RegisterFunction("AlertDialog", new AlertDialogFunction());
            ParserFunction.RegisterFunction("Speak", new SpeakFunction());
            ParserFunction.RegisterFunction("SetupSpeech", new SpeechOptionsFunction());
            ParserFunction.RegisterFunction("VoiceRecognition", new VoiceFunction());
            ParserFunction.RegisterFunction("StopVoiceRecognition", new StopVoiceFunction());
            ParserFunction.RegisterFunction("Localize", new LocalizedFunction());
            ParserFunction.RegisterFunction("TranslateTabBar", new TranslateTabBar());
            ParserFunction.RegisterFunction("InitIAP", new InitIAPFunction());
            ParserFunction.RegisterFunction("InitTTS", new InitTTSFunction());
            ParserFunction.RegisterFunction("Purchase", new PurchaseFunction());
            ParserFunction.RegisterFunction("Restore", new RestoreFunction());
            ParserFunction.RegisterFunction("ProductIdDescription", new ProductIdDescriptionFunction());
            ParserFunction.RegisterFunction("ReadFile", new ReadFileFunction());
            ParserFunction.RegisterFunction("ReadFileAsString", new ReadFileFunction(true));
            ParserFunction.RegisterFunction("Schedule", new PauseFunction(true));
            ParserFunction.RegisterFunction("CancelSchedule", new PauseFunction(false));
            ParserFunction.RegisterFunction("GetDeviceLocale", new GetDeviceLocale());
            ParserFunction.RegisterFunction("SetAppLocale", new SetAppLocale());
            ParserFunction.RegisterFunction("GetSetting", new GetSettingFunction());
            ParserFunction.RegisterFunction("SetSetting", new SetSettingFunction());
            ParserFunction.RegisterFunction("SetStyle", new SetStyleFunction());
            ParserFunction.RegisterFunction("DisplayWidth", new GadgetSizeFunction(true));
            ParserFunction.RegisterFunction("DisplayHeight", new GadgetSizeFunction(false));
            ParserFunction.RegisterFunction("Orientation", new OrientationFunction());
            ParserFunction.RegisterFunction("GetTrie", new CreateTrieFunction());
            ParserFunction.RegisterFunction("SearchTrie", new SearchTrieFunction());
            ParserFunction.RegisterFunction("ImportFile", new ImportFileFunction());
            ParserFunction.RegisterFunction("OpenUrl", new OpenURLFunction());
            ParserFunction.RegisterFunction("WebRequest", new WebRequestFunction());
            ParserFunction.RegisterFunction("SaveToPhotos", new SaveToPhotosFunction());

            ParserFunction.RegisterFunction("_ANDROID_", new CheckOSFunction(CheckOSFunction.OS.ANDROID));
            ParserFunction.RegisterFunction("_IOS_", new CheckOSFunction(CheckOSFunction.OS.IOS));
            ParserFunction.RegisterFunction("_DEVICE_INFO_", new GetDeviceInfoFunction());
            ParserFunction.RegisterFunction("_VERSION_INFO_", new GetVersionInfoFunction());
            ParserFunction.RegisterFunction("_VERSION_NUMBER_", new GetVersionNumberFunction());
            ParserFunction.RegisterFunction("CompareVersions", new CompareVersionsFunction());

            ParserFunction.RegisterFunction("Run", new RunScriptFunction());
            ParserFunction.RegisterFunction("SetOptions", new SetOptionsFunction());

            ParserFunction.RegisterFunction("GetLocalIp", new GetLocalIpFunction(true));
        }

        public static void RunScript(string fileName)
        {
            RegisterFunctions();

#if __ANDROID__
            UIVariable.WidgetTypes.Add(new DroidVariable());
#elif __IOS__
            UIVariable.WidgetTypes.Add(new iOSVariable());
#endif

            string script = FileToString(fileName);
            Variable result = null;
            try
            {
                result = Interpreter.Instance.Process(script, fileName);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception: " + exc.Message);
                Console.WriteLine(exc.StackTrace);
                ParserFunction.InvalidateStacksAfterLevel(0);
                throw;
            }
        }

        public static string FileToString(string filename)
        {
            string contents = "";
#if __ANDROID__
      Android.Content.Res.AssetManager assets = MainActivity.TheView.Assets;
      using (StreamReader sr = new StreamReader(assets.Open(filename))) {
        contents = sr.ReadToEnd();
      }
#elif __IOS__
            string[] lines = System.IO.File.ReadAllLines(filename);
            contents = string.Join("\n", lines);
#endif
            return contents;
        }

        public static void RunOnMainThread(string strAction, string arg1, string arg2)
        {
#if  __ANDROID__
            scripting.Droid.MainActivity.TheView.RunOnUiThread(() =>
            {
#elif __IOS__
            scripting.iOS.AppDelegate.GetCurrentController().InvokeOnMainThread(() =>
            {
#endif
                UIVariable.GetAction(strAction, arg1, arg2);
#if __ANDROID__ || __IOS__
            });
#endif
        }
    }

    public class RelativeSizeFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            double original = Utils.GetSafeDouble(args, 0);
            double multiplier = Utils.GetSafeDouble(args, 1);
            double relative = AutoScaleFunction.TransformSize(original,
                                AutoScaleFunction.GetRealScreenSize(), multiplier);

            return new Variable(relative);
        }
    }

    public class AutoScaleFunction : ParserFunction
    {
        public static int BASE_WIDTH = 640;

        public static double ScaleX { get; private set; }
        public static double ScaleY { get; private set; }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            ScaleX = Utils.GetSafeDouble(args, 0, 1.0);
            ScaleY = Utils.GetSafeDouble(args, 1, ScaleX);

            return Variable.EmptyInstance;
        }

        public static double GetScale(double configOverride, bool isWidth)
        {
            if (configOverride != 0.0)
            {
                return configOverride;
            }
            return isWidth ? ScaleX : ScaleY;
        }
        public static void TransformSizes(ref int width, ref int height,
                                          int screenWidth, double extra = 0.0)
        {
            int newWidth = (int)TransformSize(width, screenWidth, extra);
            if (width != 0)
            {
                double ratio = (double)newWidth / (double)width;
                height = (int)(height * ratio);
            }
            else
            {
                height = (int)TransformSize(height, screenWidth, extra);
            }
            width = newWidth;

            return;
        }
        public static double TransformSize(double size, int screenWidth, double extra = 0.0)
        {
            if (extra == 0.0)
            {
                extra = ScaleX;
                if (extra == 0.0)
                {
                    return size;
                }
            }
            //int oldSize = (int)(size * screenWidth * extra / BASE_WIDTH);
            double newSize = (size * screenWidth / BASE_WIDTH);
            double delta = (newSize - size) * extra;
            size = (size + delta);

            return size;
        }
        public static int GetRealScreenSize(bool width = true)
        {
#if __ANDROID__
      var size = UtilsDroid.GetScreenSize();
      return width ? size.Width : size.Height;
#elif __IOS__
            return width ? (int)UtilsiOS.GetRealScreenWidth() : (int)UtilsiOS.GetRealScreenHeight();
#endif
        }

        public static float ConvertFontSize(float original, int widgetWidth)
        {
            float newSize = original;
            if (widgetWidth <= 480)
            {
                newSize -= 2.5f;
            }
            else if (widgetWidth <= 540)
            {
                newSize -= 2.0f;
            }
            else if (widgetWidth <= 600)
            {
                newSize -= 1.0f;
            }
            else if (widgetWidth <= 640)
            {
                newSize -= 0f;
            }
            else if (widgetWidth <= 720)
            {
                newSize += 0.5f;
            }
            else if (widgetWidth <= 800)
            {
                newSize += 1.0f;
            }
            else if (widgetWidth <= 900)
            {
                newSize += 1.5f;
            }
            else if (widgetWidth <= 960)
            {
                newSize += 2.0f;
            }
            else if (widgetWidth <= 1024)
            {
                newSize += 2.5f;
            }
            else if (widgetWidth <= 1200)
            {
                newSize += 3.0f;
            }
            else if (widgetWidth <= 1300)
            {
                newSize += 3.5f;
            }
            else if (widgetWidth <= 1400)
            {
                newSize += 4.0f;
            }
            else
            {
                newSize += 4.0f;
            }

            return newSize;
        }
    }
    public class RunScriptFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string strScript = Utils.GetSafeString(args, 0);
            Variable result = null;

            ParserFunction.StackLevelDelta++;
            try
            {
                result = Execute(strScript);
            }
            finally
            {
                ParserFunction.StackLevelDelta--;
            }

            return result != null ? result : Variable.EmptyInstance;
        }

        public static Variable Execute(string text, string filename = "")
        {
            string[] lines = text.Split(new char[] { '\n' });

            Dictionary<int, int> char2Line;
            string includeScript = Utils.ConvertToScript(text, out char2Line);
            ParsingScript tempScript = new ParsingScript(includeScript, 0, char2Line);
            tempScript.Filename = filename;
            tempScript.OriginalScript = string.Join(Constants.END_LINE.ToString(), lines);

            Variable result = null;
            while (tempScript.Pointer < includeScript.Length)
            {
                result = tempScript.ExecuteTo();
                tempScript.GoToNextStatement();
            }
            return result;
        }
    }
    public class SetBaseWidthFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            int baseWidth = Utils.GetSafeInt(args, 0);

            Utils.CheckPosInt(baseWidth, m_name);

            AutoScaleFunction.BASE_WIDTH = baseWidth;

            return new Variable(baseWidth);
        }
    }
    public class GetRandomFunction : ParserFunction
    {
        static Random m_random = new Random();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            int limit = args[0].AsInt();
            Utils.CheckPosInt(args[0]);
            int numberRandoms = Utils.GetSafeInt(args, 1, 1);

            if (numberRandoms <= 1)
            {
                return new Variable(m_random.Next(0, limit));
            }

            List<int> available = Enumerable.Range(0, limit).ToList();
            List<Variable> result = new List<Variable>();

            for (int i = 0; i < numberRandoms && available.Count > 0; i++)
            {
                int nextRandom = m_random.Next(0, available.Count);
                result.Add(new Variable(available[nextRandom]));
                available.RemoveAt(nextRandom);
            }

            return new Variable(result);
        }
    }
    public class CreateTrieFunction : ParserFunction
    {
        static Dictionary<string, Trie> m_tries = new Dictionary<string, Trie>();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string id = Utils.GetSafeString(args, 0);

            Trie trie = null;
            if (m_tries.TryGetValue(id, out trie))
            {
                return trie;
            }
            Variable data = Utils.GetSafeVariable(args, 1, null);
            Utils.CheckNotNull(data, m_name);
            Utils.CheckNotNull(data.Tuple, m_name);

            List<string> words = new List<string>();
            for (int i = 0; i < data.Tuple.Count; i++)
            {
                words.Add(data.Tuple[i].AsString());
            }

            trie = new Trie(words);
            m_tries[id] = trie;
            return trie;
        }
    }
    public class SearchTrieFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            Trie trie = Utils.GetSafeVariable(args, 0, null) as Trie;
            Utils.CheckNotNull(trie, m_name);
            string text = args[1].AsString();
            int max = Utils.GetSafeInt(args, 2, 10);

            List<WordHint> words = new List<WordHint>();

            trie.Search(text, max, words);

            List<Variable> results = new List<Variable>(words.Count);
            foreach (WordHint word in words)
            {
                results.Add(new Variable(word.Id));
            }

            return new Variable(results);
        }
    }
    public class WebRequestFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            string uri = args[0].AsString();

            string responseFromServer = "";
            WebRequest request = WebRequest.Create(uri);

            using (WebResponse response = request.GetResponse())
            {
                Console.WriteLine("{0} status: {1}", uri,
                                  ((HttpWebResponse)response).StatusDescription);
                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                {
                    responseFromServer = sr.ReadToEnd();
                }
            }

            return new Variable(responseFromServer);
        }
    }

    public class GetLocalIpFunction : ParserFunction
    {
        bool m_usePattern;

        public GetLocalIpFunction(bool usePattern = false)
        {
            m_usePattern = usePattern;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            /*List<Variable> args = */
            script.GetFunctionArgs();
            string ip = GetIPAddress();

            if (m_usePattern)
            {
                int ind = ip.LastIndexOf(".");
                if (ind > 0)
                {
                    ip = ip.Substring(0, ind) + ".*";
                }
            }

            return new Variable(ip);
        }

        public static string GetIPAddress()
        {
            string localIP = "";
            string hostname = Dns.GetHostName();

            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                    netInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    foreach (var addrInfo in netInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (addrInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            localIP = addrInfo.Address.ToString();
                            break;
                        }
                    }
                }
            }
            return localIP;
        }
    }

    class CheckOSFunction : ParserFunction
    {
        public enum OS { NONE, IOS, ANDROID, WINDOWS_PHONE, MAC, WINDOWS };

        OS m_os;
        public CheckOSFunction(OS toCheck)
        {
            m_os = toCheck;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            bool isTheOS = false;

#if __ANDROID__
            isTheOS = m_os == OS.ANDROID;
#elif __IOS__
            isTheOS = m_os == OS.IOS;
#elif SILVERLIGHT
            isTheOS = m_os == OS.WINDOWS_PHONE;
#endif

            return new Variable(isTheOS);
        }
    }
    class GetDeviceInfoFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string deviceName = "";

#if __ANDROID__
      deviceName   = Android.OS.Build.Brand;
      string model = Android.OS.Build.Model;
      if (!model.Contains("Android")) {
        // Simulators have "Android" in both, Brand and Model.
        deviceName += " " + model;
      }
#elif __IOS__
            deviceName = UtilsiOS.GetDeviceName();
#endif
            return new Variable(deviceName);
        }
    }
    class GetVersionInfoFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string version = "";

#if __ANDROID__
      version = Android.OS.Build.VERSION.Release + " - " + 
                Android.OS.Build.VERSION.Sdk;
#elif __IOS__
            version = UIKit.UIDevice.CurrentDevice.SystemName + " " +
                      UIKit.UIDevice.CurrentDevice.SystemVersion;
#endif
            return new Variable(version);
        }
    }
    class GetVersionNumberFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
#if __ANDROID__
      string strVersion = Android.OS.Build.VERSION.Release;
#elif __IOS__
            string strVersion = UIKit.UIDevice.CurrentDevice.SystemVersion;
#endif

            return new Variable(strVersion);
        }
    }
    public class CompareVersionsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);

            string version1 = Utils.GetSafeString(args, 0);
            string version2 = Utils.GetSafeString(args, 1);

            int cmp = CompareVersions(version1, version2);

            return new Variable(cmp);
        }
        public static int CompareVersions(string version1, string version2)
        {
            if (version1 == version2)
            {
                return 0;
            }
            char[] sep = ".".ToCharArray();
            string[] parts1 = version1.Split(sep);
            string[] parts2 = version2.Split(sep);
            int commonParts = Math.Min(parts1.Length, parts2.Length);
            for (int i = 0; i < commonParts; i++)
            {
                int cmp = Compare(parts1[i], parts2[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            return parts1.Length < parts2.Length ? -1 : 1;
        }
        public static int Compare(string part1, string part2)
        {
            if (part1.Length == part2.Length)
            {
                return string.Compare(part1, part2);
            }
            return part1.Length < part2.Length ? -1 : 1;
        }
    }
}
