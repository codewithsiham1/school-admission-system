namespace SchoolAdmission.Api.Models;
public sealed class AdmissionClass
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public List<AdmissionHold> Holds { get; set; } = [];
}
