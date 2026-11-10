using System;
using System.Windows.Automation;
using System.Windows.Automation.Text;

static class ChromeFieldProbe
{
    private static void Main()
    {
        var element = AutomationElement.FocusedElement;
        for (int depth = 0; depth < 3 && element != null; depth++)
        {
            object text, value;
            bool hasText = element.TryGetCurrentPattern(TextPattern.Pattern, out text);
            bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out value);
            Console.WriteLine("{0} password={1} text={2} value={3} writable={4}",
                element.Current.ControlType.ProgrammaticName,
                element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true),
                hasText, hasValue, hasValue && !((ValuePattern)value).Current.IsReadOnly);
            if (hasText)
            {
                var ranges = ((TextPattern)text).GetSelection();
                foreach (var range in ranges ?? new TextPatternRange[0])
                {
                    Console.WriteLine("readonly={0} moved={1}", range.GetAttributeValue(TextPattern.IsReadOnlyAttribute),
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -7));
                    string result = range.GetText(8);
                    Console.WriteLine("length={0} matching={1}", result.Length, result.TrimEnd() == "ghbdtn");
                }
            }
            element = TreeWalker.ControlViewWalker.GetParent(element);
        }
    }
}
