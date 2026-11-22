using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Lines;

/// <summary>Değer satırlarını aynı hastadaki bir önceki karşılaştırılabilir değerle (aynı parametre ve birim) eşler.</summary>
public static class LabResultLineComparison
{
    /// <param name="priorResults">Önceki sonuçlar, yeniden eskiye (<see cref="Specs.LabResultsWithParameterLinesSpec"/>).</param>
    public static IReadOnlyList<LabResultLineDto> Build(IEnumerable<LabResultLine> lines, IReadOnlyList<LabResult> priorResults)
        => lines
            .OrderBy(l => l.SortOrder)
            .Select(line =>
            {
                var previous = priorResults
                    .SelectMany(r => r.Lines.Where(line.IsComparableWith).Select(p => (Result: r, Line: p)))
                    .FirstOrDefault();
                return new LabResultLineDto(
                    line.Id,
                    line.SortOrder,
                    line.ParameterName,
                    line.Value,
                    line.Unit,
                    line.ReferenceLow,
                    line.ReferenceHigh,
                    line.Flag,
                    previous.Line is null
                        ? null
                        : new LabResultLinePreviousDto(
                            previous.Result.Id,
                            previous.Result.ResultDateUtc!.Value,
                            previous.Line.Value,
                            previous.Line.Flag),
                    previous.Line is null ? null : line.TrendFrom(previous.Line.Value));
            })
            .ToList();
}
