using System.Globalization;
using System.Text.RegularExpressions;
using Clicky.App.Models;

namespace Clicky.App.Services.Chat;

public sealed class PointerInstructionParser
{
    private static readonly Regex PointInstructionRegex = new(
        @"\[POINT:(?<x>-?\d+(?:\.\d+)?),(?<y>-?\d+(?:\.\d+)?):(?<label>[^\]]*?):screen(?<screen>\d+)\]",
        RegexOptions.Compiled);

    public ParsedPointerResponse Parse(string responseText)
    {
        var parsedPointInstructions = new List<PointInstruction>();

        foreach (Match pointInstructionMatch in PointInstructionRegex.Matches(responseText))
        {
            var x = double.Parse(pointInstructionMatch.Groups["x"].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(pointInstructionMatch.Groups["y"].Value, CultureInfo.InvariantCulture);
            var label = pointInstructionMatch.Groups["label"].Value.Trim();
            var oneBasedScreenIndex = int.Parse(pointInstructionMatch.Groups["screen"].Value, CultureInfo.InvariantCulture);

            parsedPointInstructions.Add(new PointInstruction(x, y, label, oneBasedScreenIndex));
        }

        var cleanedResponseText = PointInstructionRegex.Replace(responseText, string.Empty);
        cleanedResponseText = Regex.Replace(cleanedResponseText, @"\n{3,}", "\n\n").Trim();

        return new ParsedPointerResponse(cleanedResponseText, parsedPointInstructions);
    }
}

