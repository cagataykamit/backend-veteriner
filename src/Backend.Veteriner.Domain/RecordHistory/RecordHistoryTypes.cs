namespace Backend.Veteriner.Domain.RecordHistory;

/// <summary>Değişiklik geçmişi tutulan klinik kayıt türleri (RECORD-002); değerler API sözleşmesinin parçasıdır.</summary>
public static class RecordHistoryTypes
{
    public const string Examination = "examination";
    public const string Treatment = "treatment";
    public const string Prescription = "prescription";
    public const string Vaccination = "vaccination";
    public const string LabResult = "labResult";
    public const string Administration = "administration";
    public const string Hospitalization = "hospitalization";
}
