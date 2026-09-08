using System.Text.Json;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var stateLock = new object();
var students = new List<Student>();
var instructors = new List<Instructor>();
var lectures = new List<Lecture>();
var sections = new List<Section>();
var enrollments = new List<Enrollment>();

app.MapGet("/students", () =>
{
    lock (stateLock)
    {
        return (IResult)Results.Ok(students.ToArray());
    }
});

app.MapPost("/students", (CreateStudentRequest? request) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Name))
    {
        return (IResult)Results.BadRequest(new ErrorResponse("Student name is required."));
    }

    var student = new Student(
        Guid.NewGuid(),
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim());

    lock (stateLock)
    {
        students.Add(student);
    }

    return (IResult)Results.Created($"/students/{student.Id}", student);
});

app.MapGet("/instructors", () =>
{
    lock (stateLock)
    {
        return (IResult)Results.Ok(instructors.ToArray());
    }
});

app.MapPost("/instructors", (CreateInstructorRequest? request) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Name))
    {
        return (IResult)Results.BadRequest(new ErrorResponse("Instructor name is required."));
    }

    var instructor = new Instructor(
        Guid.NewGuid(),
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim());

    lock (stateLock)
    {
        instructors.Add(instructor);
    }

    return (IResult)Results.Created($"/instructors/{instructor.Id}", instructor);
});

app.MapGet("/lectures", () =>
{
    lock (stateLock)
    {
        return (IResult)Results.Ok(lectures.ToArray());
    }
});

app.MapPost("/lectures", (CreateLectureRequest? request) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Name))
    {
        return (IResult)Results.BadRequest(new ErrorResponse("Lecture name is required."));
    }

    var lecture = new Lecture(
        Guid.NewGuid(),
        request.Name.Trim(),
        string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

    lock (stateLock)
    {
        lectures.Add(lecture);
    }

    return (IResult)Results.Created($"/lectures/{lecture.Id}", lecture);
});

app.MapGet("/sections", () =>
{
    lock (stateLock)
    {
        return (IResult)Results.Ok(sections.Select(SnapshotSection).ToArray());
    }
});

app.MapPost("/sections", (CreateSectionRequest? request) =>
{
    if (request is null || request.LectureId is not Guid lectureId || request.InstructorId is not Guid instructorId)
    {
        return (IResult)Results.BadRequest(new ErrorResponse("LectureId and InstructorId are required."));
    }

    if (!request.Capacity.HasValue || request.Capacity.Value <= 0)
    {
        return (IResult)Results.BadRequest(new ErrorResponse("Section capacity must be greater than zero."));
    }

    lock (stateLock)
    {
        if (lectures.All(lecture => lecture.Id != lectureId))
        {
            return (IResult)Results.NotFound(new ErrorResponse($"Lecture {lectureId} was not found."));
        }

        if (instructors.All(instructor => instructor.Id != instructorId))
        {
            return (IResult)Results.NotFound(new ErrorResponse($"Instructor {instructorId} was not found."));
        }

        var section = new Section
        {
            Id = Guid.NewGuid(),
            LectureId = lectureId,
            InstructorId = instructorId,
            Capacity = request.Capacity.Value
        };

        sections.Add(section);
        return (IResult)Results.Created($"/sections/{section.Id}", SnapshotSection(section));
    }
});

app.MapGet("/enrollments", () =>
{
    lock (stateLock)
    {
        return (IResult)Results.Ok(enrollments.ToArray());
    }
});

app.MapGet("/sections/{sectionId:guid}/students", (Guid sectionId) =>
{
    lock (stateLock)
    {
        if (sections.All(section => section.Id != sectionId))
        {
            return (IResult)Results.NotFound(new ErrorResponse($"Section {sectionId} was not found."));
        }

        var sectionEnrollments = enrollments
            .Where(enrollment => enrollment.SectionId == sectionId)
            .ToArray();

        return (IResult)Results.Ok(sectionEnrollments);
    }
});

app.MapPut("/sections/{sectionId:guid}/instructor", (Guid sectionId, AssignInstructorRequest? request) =>
{
    if (request?.InstructorId is not Guid instructorId)
    {
        return (IResult)Results.BadRequest(new ErrorResponse("InstructorId is required."));
    }

    return AssignInstructor(sectionId, instructorId);
});

app.MapPut("/sections/{sectionId:guid}/instructor/{instructorId:guid}",
    (Guid sectionId, Guid instructorId) => AssignInstructor(sectionId, instructorId));

app.MapPost("/sections/{sectionId:guid}/students/{studentId:guid}",
    (Guid sectionId, Guid studentId) => EnrollStudent(sectionId, studentId));

app.MapPost("/sections/{sectionId:guid}/students", (Guid sectionId, EnrollStudentRequest? request) =>
{
    if (request?.StudentId is not Guid studentId)
    {
        return (IResult)Results.BadRequest(new ErrorResponse("StudentId is required."));
    }

    return EnrollStudent(sectionId, studentId);
});

app.MapPost("/sections/{sectionId:guid}/enrollments", (Guid sectionId, EnrollStudentRequest? request) =>
{
    if (request?.StudentId is not Guid studentId)
    {
        return (IResult)Results.BadRequest(new ErrorResponse("StudentId is required."));
    }

    return EnrollStudent(sectionId, studentId);
});

app.MapDelete("/sections/{sectionId:guid}/students/{studentId:guid}",
    (Guid sectionId, Guid studentId) => RemoveStudent(sectionId, studentId));

app.MapDelete("/sections/{sectionId:guid}/enrollments/{studentId:guid}",
    (Guid sectionId, Guid studentId) => RemoveStudent(sectionId, studentId));

IResult AssignInstructor(Guid sectionId, Guid instructorId)
{
    lock (stateLock)
    {
        var section = sections.FirstOrDefault(candidate => candidate.Id == sectionId);
        if (section is null)
        {
            return Results.NotFound(new ErrorResponse($"Section {sectionId} was not found."));
        }

        if (instructors.All(instructor => instructor.Id != instructorId))
        {
            return Results.NotFound(new ErrorResponse($"Instructor {instructorId} was not found."));
        }

        section.InstructorId = instructorId;
        return Results.Ok(SnapshotSection(section));
    }
}

IResult EnrollStudent(Guid sectionId, Guid studentId)
{
    lock (stateLock)
    {
        var section = sections.FirstOrDefault(candidate => candidate.Id == sectionId);
        if (section is null)
        {
            return Results.NotFound(new ErrorResponse($"Section {sectionId} was not found."));
        }

        if (students.All(student => student.Id != studentId))
        {
            return Results.NotFound(new ErrorResponse($"Student {studentId} was not found."));
        }

        if (enrollments.Any(enrollment => enrollment.SectionId == sectionId && enrollment.StudentId == studentId))
        {
            return Results.Conflict(new ErrorResponse("The student is already enrolled in this section."));
        }

        var enrollmentCount = enrollments.Count(enrollment => enrollment.SectionId == sectionId);
        if (enrollmentCount >= section.Capacity)
        {
            return Results.Conflict(new ErrorResponse("The section has reached its capacity."));
        }

        var enrollment = new Enrollment(sectionId, studentId);
        enrollments.Add(enrollment);
        return Results.Created($"/sections/{sectionId}/students/{studentId}", enrollment);
    }
}

IResult RemoveStudent(Guid sectionId, Guid studentId)
{
    lock (stateLock)
    {
        if (sections.All(section => section.Id != sectionId))
        {
            return Results.NotFound(new ErrorResponse($"Section {sectionId} was not found."));
        }

        if (students.All(student => student.Id != studentId))
        {
            return Results.NotFound(new ErrorResponse($"Student {studentId} was not found."));
        }

        var enrollment = enrollments.FirstOrDefault(candidate =>
            candidate.SectionId == sectionId && candidate.StudentId == studentId);

        if (enrollment is null)
        {
            return Results.NotFound(new ErrorResponse("The student is not enrolled in this section."));
        }

        enrollments.Remove(enrollment);
        return Results.NoContent();
    }
}

Section SnapshotSection(Section section) => new()
{
    Id = section.Id,
    LectureId = section.LectureId,
    InstructorId = section.InstructorId,
    Capacity = section.Capacity
};

app.Run();

public sealed record Student(Guid Id, string Name, string? Email);

public sealed record Instructor(Guid Id, string Name, string? Email);

public sealed record Lecture(Guid Id, string Name, string? Description);

public sealed class Section
{
    public Guid Id { get; init; }

    public Guid LectureId { get; init; }

    public Guid InstructorId { get; set; }

    public int Capacity { get; init; }
}

public sealed record Enrollment(Guid SectionId, Guid StudentId);

public sealed class CreateStudentRequest
{
    public string? Name { get; set; }

    public string? Email { get; set; }
}

public sealed class CreateInstructorRequest
{
    public string? Name { get; set; }

    public string? Email { get; set; }
}

public sealed class CreateLectureRequest
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}

public sealed class CreateSectionRequest
{
    public Guid? LectureId { get; set; }

    public Guid? InstructorId { get; set; }

    public int? Capacity { get; set; }
}

public sealed class AssignInstructorRequest
{
    public Guid? InstructorId { get; set; }
}

public sealed class EnrollStudentRequest
{
    public Guid? StudentId { get; set; }
}

public sealed record ErrorResponse(string Error);
