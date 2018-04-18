using System;
using System.Collections.Generic;


namespace SplitAndMerge
{
  public class UIVariable : Variable
  {
    public Action<string, string> ActionDelegate;

    public static List<UIVariable> WidgetTypes = new List<UIVariable>();

    protected static int m_currentTag;

    public enum UIType
    {
      NONE, LOCATION, VIEW, BUTTON, LABEL, TEXT_FIELD, TEXT_VIEW, EDIT_VIEW, PICKER_VIEW, PICKER_IMAGES,
      LIST_VIEW, COMBOBOX, IMAGE_VIEW, SWITCH, SLIDER, STEPPER, SEGMENTED, CUSTOM
    };
    public UIVariable()
    {
      WidgetType = UIType.NONE;
    }
    public UIVariable(UIType type, string name = "",
                      UIVariable refViewX = null, UIVariable refViewY = null)
    {
      WidgetType = type;
      WidgetName = name;
      RefViewX = refViewX;
      RefViewY = refViewY;
    }

    public override Variable Clone()
    {
      UIVariable newVar = (UIVariable)this.MemberwiseClone();
      return newVar;
    }

    public void SetSize(int width, int height)
    {
      Width = width;
      Height = height;
    }
    public void SetRules(string ruleX, string ruleY)
    {
      RuleX = ruleX;
      RuleY = ruleY;
    }
    public string Name {
      get { return WidgetName; }
    }

    public UIType WidgetType { get; set; }
    public string WidgetName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int TranslationX { get; set; }
    public int TranslationY { get; set; }

    public string RuleX { get; set; }
    public string RuleY { get; set; }

    public UIVariable Location { get; set; }
    public UIVariable RefViewX { get; set; }
    public UIVariable RefViewY { get; set; }
    public UIVariable ParentView { get; set; }

    public Variable InitValue { get; set; }
    public int Alignment { get; set; }
    public double MinVal { get; set; }
    public double MaxVal { get; set; }
    public double CurrVal { get; set; }
    public double Step { get; set; }

    public override string AsString(bool isList = true, bool sameLine = true)
    {
      string baseStr = base.AsString(isList, sameLine);
      if (!string.IsNullOrEmpty(baseStr)) {
        return baseStr;
      }
      return WidgetName;
    }

    public static Variable GetAction(string funcName, string senderName, string eventArg)
    {
      if (senderName == "") {
        senderName = "\"\"";
      }
      if (eventArg == "") {
        eventArg = "\"\"";
      }
      string body = string.Format("{0}({1},{2});", funcName, senderName, eventArg);

      ParsingScript tempScript = new ParsingScript(body);
      Variable result = tempScript.ExecuteTo();

      return result;
    }
  }
}

