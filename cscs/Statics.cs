using System;
namespace SplitAndMerge
{
  public class Statics
  {
    public static string ProcessClick(string arg)
    {
      var now = DateTime.Now.ToString("T");
      return "Clicks: " + arg + "\n" + now;
    }
  }
}
