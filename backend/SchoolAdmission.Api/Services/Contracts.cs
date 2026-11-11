namespace SchoolAdmission.Api.Services;
public sealed record CreateClassRequest(string Name, int TotalSeats);
public sealed record ClassResponse(string Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
public sealed record HoldResponse(string HoldId, string ClassId, string StudentId, string Status, DateTime ExpiresAt);
public sealed record HoldStatusResponse(string HoldId, string ClassId, string Status, DateTime ExpiresAt);
