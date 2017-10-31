using System;
using System.Collections.Generic;
using System.Linq;
using SplitAndMerge;

#if __ANDROID__
using scripting.Droid;
#endif
#if __IOS__
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
      ParserFunction.RegisterFunction("AddImageView", new AddWidgetFunction("ImageView"));
      ParserFunction.RegisterFunction("AddTypePickerView", new AddWidgetFunction("TypePicker"));
      ParserFunction.RegisterFunction("AddSwitch", new AddWidgetFunction("Switch"));
      ParserFunction.RegisterFunction("AddSlider", new AddWidgetFunction("Slider"));
      ParserFunction.RegisterFunction("AddStepper", new AddWidgetFunction("Stepper"));
      ParserFunction.RegisterFunction("AddStepperLeft", new AddWidgetFunction("Stepper", "left"));
      ParserFunction.RegisterFunction("AddStepperRight", new AddWidgetFunction("Stepper", "right"));
      ParserFunction.RegisterFunction("AddListView", new AddWidgetFunction("ListView"));
      ParserFunction.RegisterFunction("AddCombobox", new AddWidgetFunction("Combobox"));
      ParserFunction.RegisterFunction("AddSegmentedControl", new AddWidgetFunction("SegmentedControl"));

      ParserFunction.RegisterFunction("AddWidgetData", new AddWidgetDataFunction());
      ParserFunction.RegisterFunction("AddWidgetImages", new AddWidgetImagesFunction());
      ParserFunction.RegisterFunction("AddTab", new AddTabFunction());
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
      ParserFunction.RegisterFunction("MoveView", new MoveViewFunction());
      ParserFunction.RegisterFunction("SetBackgroundColor", new SetBackgroundColorFunction());
      ParserFunction.RegisterFunction("SetBackground", new SetBackgroundImageFunction());
      ParserFunction.RegisterFunction("SetText", new SetTextFunction());
      ParserFunction.RegisterFunction("GetText", new GetTextFunction());
      ParserFunction.RegisterFunction("SetValue", new SetValueFunction());
      ParserFunction.RegisterFunction("GetValue", new GetValueFunction());
      ParserFunction.RegisterFunction("SetImage", new SetImageFunction());
      ParserFunction.RegisterFunction("SetFontColor", new SetFontColorFunction());
      ParserFunction.RegisterFunction("SetFontSize", new SetFontSizeFunction());
      ParserFunction.RegisterFunction("AlignText", new AlignTitleFunction());
      ParserFunction.RegisterFunction("SetSize", new SetSizeFunction());

      ParserFunction.RegisterFunction("AddAction", new AddActionFunction());
      ParserFunction.RegisterFunction("OnOrientationChange", new OrientationChangeFunction());
      ParserFunction.RegisterFunction("ShowToast", new ShowToastFunction());
      ParserFunction.RegisterFunction("AlertDialog", new AlertDialogFunction());
      ParserFunction.RegisterFunction("CallNative", new InvokeNativeFunction());
      ParserFunction.RegisterFunction("Speak", new SpeakFunction());
      ParserFunction.RegisterFunction("SetupSpeech", new SpeechOptionsFunction());
      ParserFunction.RegisterFunction("VoiceRecognition", new VoiceFunction());
      ParserFunction.RegisterFunction("StopVoiceRecognition", new StopVoiceFunction());
      ParserFunction.RegisterFunction("Localize", new LocalizedFunction());
      ParserFunction.RegisterFunction("TranslateTabBar", new TranslateTabBar());
      ParserFunction.RegisterFunction("InitAds", new InitAds());
      ParserFunction.RegisterFunction("ShowInterstitial", new ShowInterstitial());
      ParserFunction.RegisterFunction("AddBanner", new AddWidgetFunction("AdMobBanner"));
      ParserFunction.RegisterFunction("InitIAP", new InitIAPFunction());
      ParserFunction.RegisterFunction("InitTTS", new InitTTSFunction());
      ParserFunction.RegisterFunction("Purchase", new PurchaseFunction());
      ParserFunction.RegisterFunction("Restore", new RestoreFunction());
      ParserFunction.RegisterFunction("ProductIdDescription", new ProductIdDescriptionFunction());
      ParserFunction.RegisterFunction("ReadFile", new ReadFileFunction());
      ParserFunction.RegisterFunction("Schedule", new PauseFunction(true));
      ParserFunction.RegisterFunction("CancelSchedule", new PauseFunction(false));
      ParserFunction.RegisterFunction("GetDeviceLocale", new GetDeviceLocale());
      ParserFunction.RegisterFunction("SetAppLocale", new SetAppLocale());
      ParserFunction.RegisterFunction("GetSetting", new GetSettingFunction());
      ParserFunction.RegisterFunction("SetSetting", new SetSettingFunction());
      ParserFunction.RegisterFunction("DisplayWidth", new GadgetSizeFunction(true));
      ParserFunction.RegisterFunction("DisplayHeight", new GadgetSizeFunction(false));
      ParserFunction.RegisterFunction("Orientation", new OrientationFunction());
      ParserFunction.RegisterFunction("GetTrie", new CreateTrieFunction());
      ParserFunction.RegisterFunction("SearchTrie", new SearchTrieFunction());
      ParserFunction.RegisterFunction("ImportFile", new ImportFileFunction());
      ParserFunction.RegisterFunction("OpenUrl", new OpenURLFunction());

      ParserFunction.RegisterFunction("_ANDROID_", new CheckOSFunction(CheckOSFunction.OS.ANDROID));
      ParserFunction.RegisterFunction("_IOS_", new CheckOSFunction(CheckOSFunction.OS.IOS));
      ParserFunction.RegisterFunction("_VERSION_", new GetVersionFunction());
      ParserFunction.RegisterFunction("_VERSION_NUMBER_", new GetVersionNumberFunction());
      ParserFunction.RegisterFunction("CompareVersions", new CompareVersionsFunction());

      ParserFunction.RegisterFunction("SetOptions", new SetOptionsFunction());


    }
  }

  public class AutoScaleFunction : ParserFunction
  {
    public static int BASE_WIDTH = 640;

    public static double ScaleX { get; private set; }
    public static double ScaleY { get; private set; }

    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
          Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);

      ScaleX = Utils.GetSafeDouble(args, 0);
      ScaleY = Utils.GetSafeDouble(args, 1, ScaleX);

      return Variable.EmptyInstance;
    }

    public static double GetScale(double configOverride, bool isWidth)
    {
      if (configOverride != 0.0) {
        return configOverride;
      }
      return isWidth ? ScaleX : ScaleY;
    }
    public static void TransformSizes(ref int width, ref int height, int screenWidth, string option, double extra = 0.0)
    {
      if (!string.IsNullOrWhiteSpace(option) && option != "auto") {
        return;
      }
      /*if (extra == 0.0) {
        extra = ScaleX;
        if (extra == 0.0) {
          return;
        }
      }*/

      int newWidth = TransformSize(width, screenWidth, extra);
      if (width != 0) {
        double ratio = (double)newWidth / (double)width;
        height = (int)(height * ratio);
      } else {
        height = TransformSize(height, screenWidth, extra);
      }
      width = newWidth;

      return;
    }
    public static int TransformSize(int size, int screenWidth, double extra)
    {
      //if (screenWidth <= BASE_WIDTH) {
      //  return size;
      //}
      if (extra == 0.0) {
        extra = ScaleX;
        if (extra == 0.0) {
          return size;
        }
      }
      return (int)(size * screenWidth * extra / BASE_WIDTH);
    }  }
  public class SetBaseWidthFunction : ParserFunction
  {

    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);

      int baseWidth = Utils.GetSafeInt(args, 0);

      Utils.CheckPosInt(baseWidth, m_name);

      AutoScaleFunction.BASE_WIDTH = baseWidth;

      return new Variable(baseWidth);
    }
  }
  public class InvokeNativeFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      string methodName = Utils.GetItem(script).AsString();
      Utils.CheckNotEmpty(script, methodName, m_name);
      script.MoveForwardIf(Constants.NEXT_ARG);

      string paramName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
      Utils.CheckNotEmpty(script, paramName, m_name);
      script.MoveForwardIf(Constants.NEXT_ARG);

      Variable paramValueVar = Utils.GetItem(script);
      string paramValue = paramValueVar.AsString();

      var result = Utils.InvokeCall(typeof(Statics),
                                    methodName, paramName, paramValue);
      return result;
    }
  }
  public class GetRandomFunction : ParserFunction
  {
    static Random m_random = new Random();

    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);
      int limit = args[0].AsInt();
      Utils.CheckPosInt(args[0]);
      int numberRandoms = Utils.GetSafeInt(args, 1, 1);

      if (numberRandoms <= 1) {
        return new Variable(m_random.Next(0, limit));
      }

      List<int> available = Enumerable.Range(0, limit).ToList();
      List<Variable> result = new List<Variable>();

      for (int i = 0; i < numberRandoms && available.Count > 0; i++) {
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
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);

      string id = Utils.GetSafeString(args, 0);

      Trie trie = null;
      if (m_tries.TryGetValue(id, out trie)) {
        return trie;
      }
      Variable data = Utils.GetSafeVariable(args, 1, null);
      Utils.CheckNotNull(data, m_name);
      Utils.CheckNotNull(data.Tuple, m_name);

      List<string> words = new List<string>();
      for (int i = 0; i < data.Tuple.Count; i++) {
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
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 2, m_name);

      Trie trie = Utils.GetSafeVariable(args, 0, null) as Trie;
      Utils.CheckNotNull(trie, m_name);
      string text = args[1].AsString();
      int max = Utils.GetSafeInt(args, 2, 7);

      List<WordHint> words = new List<WordHint>();

      trie.Search(text, max, words);

      List<Variable> results = new List<Variable>(words.Count);
      foreach (WordHint word in words) {
        results.Add(new Variable(word.Id));
      }

      return new Variable(results);
    }
  }
  public class ProductIdDescriptionFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 1, m_name);
      string productId = args[0].AsString();

      string description = IAP.GetDescription(productId);

      return new Variable(description);
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
#endif
#if __IOS__
      isTheOS = m_os == OS.IOS;
#endif
#if SILVERLIGHT
            isTheOS = m_os == OS.WINDOWS_PHONE;
#endif

      return new Variable(isTheOS);
    }
  }
  class GetVersionFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      string version = "";

#if __ANDROID__
      version = Android.OS.Build.Brand + " " + Android.OS.Build.VERSION.Release +
                " - " + Android.OS.Build.VERSION.Sdk;
#endif
#if __IOS__
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
#endif
#if __IOS__
      string strVersion = UIKit.UIDevice.CurrentDevice.SystemVersion;
#endif

      return new Variable(strVersion);
    }
  }
  public class CompareVersionsFunction : ParserFunction
  {
    protected override Variable Evaluate(ParsingScript script)
    {
      bool isList = false;
      List<Variable> args = Utils.GetArgs(script,
                            Constants.START_ARG, Constants.END_ARG, out isList);
      Utils.CheckArgs(args.Count, 2, m_name);

      string version1 = Utils.GetSafeString(args, 0);
      string version2 = Utils.GetSafeString(args, 1);

      int cmp = CompareVersions(version1, version2);

      return new Variable(cmp);
    }
    public static int CompareVersions(string version1, string version2)
    {
      if (version1 == version2) {
        return 0;
      }
      char[] sep = ".".ToCharArray();
      string[] parts1 = version1.Split(sep);
      string[] parts2 = version2.Split(sep);
      int commonParts = Math.Min(parts1.Length, parts2.Length);
      for (int i = 0; i < commonParts; i++) {
        int cmp = Compare(parts1[i], parts2[i]);
        if (cmp != 0) {
          return cmp;
        }
      }
      return parts1.Length < parts2.Length ? -1 : 1;
    }
    public static int Compare(string part1, string part2)
    {
      if (part1.Length == part2.Length) {
        return string.Compare(part1, part2);
      }
      return part1.Length < part2.Length ? -1 : 1;
    }
  }
}
