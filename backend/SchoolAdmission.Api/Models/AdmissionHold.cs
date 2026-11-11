namespace SchoolAdmission.Api.Models;
public sealed class AdmissionHold
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClassId { get; set; }
    public AdmissionClass? Class { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public string Status { get; set; } = "HELD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
