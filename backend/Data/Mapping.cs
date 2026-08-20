using HelloDoctor.Api.Models;

namespace HelloDoctor.Api.Data;

public static class Mapping
{
    public static UserDto ToDto(this User u) => new(
        u.Id, u.Email, u.FullName, u.Role.ToString(), u.AvatarColor,
        u.Specialty, u.Title, u.Rating, u.ExperienceYears, u.Bio, u.Age, u.BloodType);
}
