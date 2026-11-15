using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports;

/// <summary>
/// Yüklemedeki hazırlık satırlarını toplu yazar (E-vet yedeğinde ~50 bin satır; tek tek eklemek yüklemeyi dakikalarca
/// uzatıyordu). Komutun açık transaction'ına katılır: komut geri alınırsa satırlar da yazılmamış olur.
/// </summary>
public interface IImportBatchRowBulkWriter
{
    Task InsertAsync(IReadOnlyCollection<ImportBatchRow> rows, CancellationToken ct);
}
