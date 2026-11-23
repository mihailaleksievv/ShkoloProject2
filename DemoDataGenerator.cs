// Owner: Participant 3 – Data Developer
using Shkolo.Application;
using Shkolo.Application.Abstractions;
using Shkolo.Domain;
using Shkolo.Domain.Enums;
using Shkolo.Domain.Models;

namespace Shkolo.Infrastructure.Persistence;

/// <summary>Създава демонстрационната база при първото стартиране.</summary>
public sealed class DemoDataGenerator
{
    private const int LessonsPerDay = 6;
    private const int HistoryDays = 42;

    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;
    private readonly Random _rng = new(2026);
    private readonly SchoolDatabase _db = new() { SchoolName = DemoNames.SchoolName };

    private AcademicYear _year = null!;
    private DateOnly _anchor;
    private Guid _adminUserId;

    public DemoDataGenerator(IClock clock, IPasswordHasher hasher)
    {
        _clock = clock;
        _hasher = hasher;
    }

    public SchoolDatabase Generate()
    {
        CreateYearAndBells();
        var teachers = CreatePeople();
        var subjects = CreateSubjects();
        var classes = CreateClassesAndStudents(teachers);
        CreateAssignmentsAndTimetable(teachers, subjects, classes);
        CreateLessonsAttendanceAndGrades();
        CreateTermGrades();
        CreateHomework(classes);
        CreateFeedbackAndAchievements(teachers, classes);
        CreateFeaturedStudentStory(teachers);
        CreateAnnouncementsAndEvents(teachers, classes);
        CreateNotifications();
        _db.AuditLog.Add(new AuditEntry
        {
            UserId = _adminUserId,
            Action = "System.Seeded",
            EntityType = "SchoolDatabase",
            OccurredAtUtc = _clock.UtcNow,
            Summary = "Създадена е демонстрационна база с измислени данни.",
        });
        return _db;
    }

    private void CreateYearAndBells()
    {
        var today = _clock.Today;
        var startYear = today >= new DateOnly(today.Year, 9, 15) ? today.Year : today.Year - 1;
        _year = new AcademicYear
        {
            Name = $"{startYear}/{startYear + 1}",
            StartDate = new DateOnly(startYear, 9, 15),
            FirstTermEndDate = new DateOnly(startYear + 1, 1, 31),
            EndDate = new DateOnly(startYear + 1, 6, 30),
            IsActive = true,
        };
        _db.AcademicYears.Add(_year);

        // Dated activity is generated up to today, or up to the end of the year during the summer.
        _anchor = today < _year.EndDate ? today : _year.EndDate;

        var start = new TimeOnly(8, 0);
        for (var n = 1; n <= 7; n++)
        {
            _db.BellSchedule.Add(new LessonSlot { LessonNumber = n, StartTime = start, EndTime = start.AddMinutes(40) });
            start = start.AddMinutes(n == 3 ? 60 : 50);
        }
    }

    private List<Teacher> CreatePeople()
    {
        _adminUserId = AddUser("admin", "Администратор", "Admin123!", UserRole.Administrator, null, null).Id;
        var teachers = new List<Teacher>();
        for (var i = 0; i < DemoNames.Teachers.Length; i++)
        {
            var (first, last, email) = DemoNames.Teachers[i];
            var teacher = new Teacher { EmployeeNumber = $"T-{101 + i}", FirstName = first, LastName = last, Email = email, CreatedAtUtc = _clock.UtcNow };
            _db.Teachers.Add(teacher);
            AddUser($"teacher{i + 1}", teacher.FullName, "Teacher123!", UserRole.Teacher, null, teacher.Id);
            teachers.Add(teacher);
        }

        return teachers;
    }

    private Dictionary<string, Subject> CreateSubjects()
    {
        var subjects = new Dictionary<string, Subject>();
        foreach (var name in DemoNames.TeacherSubjects.SelectMany(s => s))
        {
            var subject = new Subject { Name = name, Code = DemoNames.SubjectCodes[name] };
            _db.Subjects.Add(subject);
            subjects[name] = subject;
        }

        return subjects;
    }

    private List<SchoolClass> CreateClassesAndStudents(List<Teacher> teachers)
    {
        var classes = new List<SchoolClass>();
        var studentIndex = 0;
        for (var c = 0; c < DemoNames.Classes.Length; c++)
        {
            var (grade, section, _) = DemoNames.Classes[c];
            var cls = new SchoolClass { GradeLevel = grade, Section = section, AcademicYearId = _year.Id, ClassTeacherId = teachers[(c + 2) % teachers.Count].Id };
            _db.Classes.Add(cls);
            classes.Add(cls);

            for (var s = 0; s < 6; s++, studentIndex++)
            {
                var (first, last) = DemoNames.Students[studentIndex];
                var student = new Student
                {
                    StudentNumber = (1001 + studentIndex).ToString(),
                    FirstName = first,
                    LastName = last,
                    ClassId = cls.Id,
                    CreatedAtUtc = _clock.UtcNow,
                };
                _db.Students.Add(student);
                AddUser($"student{studentIndex + 1}", student.FullName, "Student123!", UserRole.Student, student.Id, null);
            }
        }

        return classes;
    }

    /// <summary>
    /// Latin-square rotation: in every period each teacher is in a different class, so the timetable
    /// is conflict-free by construction and every class gets exactly 10 periods a week from each teacher.
    /// </summary>
    private void CreateAssignmentsAndTimetable(List<Teacher> teachers, Dictionary<string, Subject> subjects, List<SchoolClass> classes)
    {
        foreach (var cls in classes)
        {
            for (var t = 0; t < teachers.Count; t++)
            {
                foreach (var name in DemoNames.TeacherSubjects[t])
                {
                    _db.TeacherAssignments.Add(new TeacherAssignment { TeacherId = teachers[t].Id, ClassId = cls.Id, SubjectId = subjects[name].Id });
                }
            }
        }

        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        for (var c = 0; c < classes.Count; c++)
        {
            var taught = new int[teachers.Count];
            for (var slot = 0; slot < days.Length * LessonsPerDay; slot++)
            {
                var t = (slot + c) % teachers.Count;

                // Teacher's 10 weekly periods cycle through their subjects: A B C A B C A B C A (A = 4 hours).
                var subjectName = DemoNames.TeacherSubjects[t][taught[t]++ % 3];
                var isSport = subjectName.StartsWith("Физическо", StringComparison.Ordinal);
                _db.Timetable.Add(new TimetableEntry
                {
                    ClassId = classes[c].Id,
                    SubjectId = subjects[subjectName].Id,
                    TeacherId = teachers[t].Id,
                    DayOfWeek = days[slot / LessonsPerDay],
                    LessonNumber = slot % LessonsPerDay + 1,
                    Room = isSport ? "Салон" : DemoNames.Classes[c].Room,
                });
            }
        }
    }

    private void CreateLessonsAttendanceAndGrades()
    {
        var from = _anchor.AddDays(-HistoryDays) < _year.StartDate ? _year.StartDate : _anchor.AddDays(-HistoryDays);
        var topicCounters = new Dictionary<(Guid, Guid), int>();
        var ability = _db.Students.ToDictionary(s => s.Id, _ => 3.2 + _rng.NextDouble() * 2.6);
        var gradable = new Dictionary<(Guid Student, Guid Subject), List<Lesson>>();

        // History ends yesterday: today's lessons are left for the teacher to record live in the demo.
        var lastDay = _anchor == _clock.Today ? _anchor.AddDays(-1) : _anchor;
        for (var date = from; date <= lastDay; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var entry in _db.Timetable.Where(t => t.DayOfWeek == date.DayOfWeek).OrderBy(t => t.LessonNumber).ToList())
            {
                var key = (entry.ClassId, entry.SubjectId);
                var index = topicCounters.GetValueOrDefault(key);
                topicCounters[key] = index + 1;
                var topics = DemoNames.Topics[_db.Subjects.First(s => s.Id == entry.SubjectId).Name];
                var lesson = new Lesson
                {
                    TimetableEntryId = entry.Id,
                    ClassId = entry.ClassId,
                    SubjectId = entry.SubjectId,
                    TeacherId = entry.TeacherId,
                    Date = date,
                    LessonNumber = entry.LessonNumber,
                    Topic = topics[index % topics.Length],
                    RecordedAtUtc = ToUtc(date, 14),
                };
                _db.Lessons.Add(lesson);

                foreach (var student in _db.Students.Where(s => s.ClassId == entry.ClassId))
                {
                    var status = RandomAttendance();
                    _db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        StudentId = student.Id,
                        LessonId = lesson.Id,
                        Status = status,
                        Note = status == AttendanceStatus.ExcusedAbsent ? "Медицинска бележка" : null,
                        RecordedByTeacherId = entry.TeacherId,
                        RecordedAtUtc = lesson.RecordedAtUtc,
                    });

                    if (status is AttendanceStatus.Present or AttendanceStatus.Late)
                    {
                        var list = gradable.TryGetValue((student.Id, entry.SubjectId), out var existing) ? existing : gradable[(student.Id, entry.SubjectId)] = [];
                        list.Add(lesson);
                    }
                }
            }
        }

        // Insertion order is deterministic; ordering by Guid would not be (ids differ between runs).
        foreach (var ((studentId, _), lessons) in gradable)
        {
            var count = Math.Min(lessons.Count, _rng.Next(3, 6));
            var picked = lessons.OrderBy(_ => _rng.Next()).Take(count).OrderBy(l => l.Date).ToList();
            foreach (var lesson in picked)
            {
                var raw = ability[studentId] + (_rng.NextDouble() - 0.5) * 1.6;
                _db.Grades.Add(new Grade
                {
                    StudentId = studentId,
                    ClassId = lesson.ClassId,
                    SubjectId = lesson.SubjectId,
                    TeacherId = lesson.TeacherId,
                    LessonId = lesson.Id,
                    Value = Math.Clamp((decimal)Math.Round(raw, MidpointRounding.AwayFromZero), DomainRules.MinGrade, DomainRules.MaxGrade),
                    Date = lesson.Date,
                    GradeType = RandomGradeType(),
                    CreatedAtUtc = ToUtc(lesson.Date, 15),
                });
            }
        }
    }

    private void CreateTermGrades()
    {
        if (_clock.Today <= _year.FirstTermEndDate)
        {
            return;
        }

        foreach (var group in _db.Grades.Where(g => g.Date <= _year.FirstTermEndDate).GroupBy(g => (g.StudentId, g.SubjectId, g.ClassId)))
        {
            var teacherId = _db.TeacherAssignments.First(a => a.ClassId == group.Key.ClassId && a.SubjectId == group.Key.SubjectId).TeacherId;
            _db.TermGrades.Add(new TermGrade
            {
                StudentId = group.Key.StudentId,
                SubjectId = group.Key.SubjectId,
                ClassId = group.Key.ClassId,
                AcademicYearId = _year.Id,
                Period = TermPeriod.FirstTerm,
                Value = DomainRules.RoundToTermGrade(group.Average(g => g.Value)),
                TeacherId = teacherId,
                RecordedAtUtc = ToUtc(_year.FirstTermEndDate, 16),
            });
        }
    }

    private void CreateHomework(List<SchoolClass> classes)
    {
        var plans = new (int AssignedDaysAgo, int DueInDays, bool Active, string Title)[]
        {
            (12, -5, true, "Упражнение върху изучения материал"),
            (2, 5, true, "Задачи за самостоятелна работа"),
            (1, 9, true, "Подготовка за контролна работа"),
            (25, -18, false, "Реферат"),
        };

        foreach (var cls in classes)
        {
            var assignments = _db.TeacherAssignments.Where(a => a.ClassId == cls.Id).ToList();
            for (var i = 0; i < plans.Length; i++)
            {
                var plan = plans[i];
                var assignment = assignments[(i * 4 + cls.GradeLevel) % assignments.Count];
                var assigned = Max(_anchor.AddDays(-plan.AssignedDaysAgo), _year.StartDate);
                var homework = new Homework
                {
                    ClassId = cls.Id,
                    SubjectId = assignment.SubjectId,
                    TeacherId = assignment.TeacherId,
                    Title = $"{plan.Title} – {_db.Subjects.First(s => s.Id == assignment.SubjectId).Name}",
                    Description = "Вижте материала от последните часове и решете задачите в тетрадката.",
                    AssignedDate = assigned,
                    DueDate = Max(_anchor.AddDays(plan.DueInDays), assigned),
                    IsActive = plan.Active,
                    CreatedAtUtc = ToUtc(assigned, 15),
                };
                _db.Homework.Add(homework);

                var submitShare = homework.DueDate < _anchor ? 0.6 : 0.3;
                foreach (var student in _db.Students.Where(s => s.ClassId == cls.Id))
                {
                    if (_rng.NextDouble() < submitShare)
                    {
                        var day = Min(homework.AssignedDate.AddDays(1), _anchor.AddDays(-1));
                        _db.HomeworkSubmissions.Add(new HomeworkSubmission
                        {
                            HomeworkId = homework.Id,
                            StudentId = student.Id,
                            Content = "Решенията са в тетрадката, снимка е предадена на учителя.",
                            SubmittedAtUtc = ToUtc(Max(day, homework.AssignedDate), 18),
                        });
                    }
                }
            }
        }
    }

    private void CreateFeedbackAndAchievements(List<Teacher> teachers, List<SchoolClass> classes)
    {
        foreach (var cls in classes)
        {
            var students = _db.Students.Where(s => s.ClassId == cls.Id).ToList();
            for (var i = 0; i < 3; i++)
            {
                var praise = i != 1;
                _db.Feedback.Add(new Feedback
                {
                    StudentId = students[_rng.Next(students.Count)].Id,
                    TeacherId = teachers[_rng.Next(teachers.Count)].Id,
                    Type = praise ? FeedbackType.Praise : FeedbackType.Remark,
                    Description = praise ? DemoNames.Praises[_rng.Next(DemoNames.Praises.Length)] : DemoNames.Remarks[_rng.Next(DemoNames.Remarks.Length)],
                    Date = Max(_anchor.AddDays(-_rng.Next(1, 20)), _year.StartDate),
                    IsVisibleToStudent = !(cls.Section == "Б" && !praise),
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
        }

        foreach (var (name, description, symbol) in DemoNames.Badges)
        {
            _db.AchievementBadges.Add(new AchievementBadge { Name = name, Description = description, Symbol = symbol });
        }

        for (var i = 0; i < 6; i++)
        {
            var student = _db.Students[_rng.Next(_db.Students.Count)];
            _db.StudentAchievements.Add(new StudentAchievement
            {
                BadgeId = _db.AchievementBadges[i].Id,
                StudentId = student.Id,
                AssignedByTeacherId = teachers[i % teachers.Count].Id,
                AssignedAtUtc = ToUtc(Max(_anchor.AddDays(-_rng.Next(1, 25)), _year.StartDate), 12),
                Note = "За постигнатото през срока.",
            });
        }
    }

    /// <summary>
    /// Fixed praise, remarks and badges for the first student (student1), so the demo always has a full
    /// feedback history to show. Uses no random numbers, so the rest of the demo data stays the same.
    /// </summary>
    private void CreateFeaturedStudentStory(List<Teacher> teachers)
    {
        var student = _db.Students[0];
        foreach (var (teacher, praise, daysAgo, text) in DemoNames.FeaturedStudentFeedback)
        {
            _db.Feedback.Add(new Feedback
            {
                StudentId = student.Id,
                TeacherId = teachers[teacher].Id,
                Type = praise ? FeedbackType.Praise : FeedbackType.Remark,
                Description = text,
                Date = Max(_anchor.AddDays(-daysAgo), _year.StartDate),
                IsVisibleToStudent = true,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        // Two badges for the same student: "Отлично представяне" and "Работа в екип".
        foreach (var (badge, teacher, daysAgo) in new[] { (1, 0, 3), (4, 0, 15) })
        {
            if (_db.StudentAchievements.Any(a => a.StudentId == student.Id && a.BadgeId == _db.AchievementBadges[badge].Id))
            {
                continue;
            }

            _db.StudentAchievements.Add(new StudentAchievement
            {
                BadgeId = _db.AchievementBadges[badge].Id,
                StudentId = student.Id,
                AssignedByTeacherId = teachers[teacher].Id,
                AssignedAtUtc = ToUtc(Max(_anchor.AddDays(-daysAgo), _year.StartDate), 12),
                Note = badge == 1 ? "За най-добрия резултат на контролната по математика." : "За помощта на съучениците при задачите с цикли.",
            });
        }
    }

    private void CreateAnnouncementsAndEvents(List<Teacher> teachers, List<SchoolClass> classes)
    {
        var teacherUser = teachers.Select(t => _db.Users.First(u => u.TeacherId == t.Id).Id).ToList();
        var announcements = new (Guid Author, Guid? Target, string Title, string Body, int DaysAgo)[]
        {
            (_adminUserId, null, "Добре дошли в SHKOLO", "Електронният дневник на училището вече работи. Следете оценките, отсъствията и домашните си тук.", 30),
            (_adminUserId, null, "Коледна ваканция", "Коледната ваканция е от 22 декември до 4 януари включително.", 3),
            (teacherUser[0], classes[1].Id, "Контролна работа по математика", "Контролната работа ще обхване квадратните уравнения и формулите на Виет.", 2),
            (teacherUser[1], classes[2].Id, "Литературен конкурс", "Приемат се есета на тема „Моят град“ до края на месеца.", 5),
        };
        foreach (var a in announcements)
        {
            _db.Announcements.Add(new Announcement { AuthorUserId = a.Author, TargetClassId = a.Target, Title = a.Title, Body = a.Body, PublishedAtUtc = _clock.UtcNow.AddDays(-a.DaysAgo) });
        }

        var now = _clock.LocalNow.Date;
        var events = new (int InDays, int Hour, int Hours, Guid? Target, Guid Creator, string Title, string Description)[]
        {
            (-10, 9, 4, null, _adminUserId, "Есенен спортен празник", "Състезания по класове на стадиона."),
            (3, 18, 2, null, _adminUserId, "Родителска среща", "Обща родителска среща в актовата зала."),
            (6, 10, 1, classes[1].Id, teacherUser[0], "Контролна работа по математика", "Квадратни уравнения – 3. час."),
            (12, 9, 5, null, _adminUserId, "Олимпиада по информатика", "Общински кръг в компютърен кабинет 305."),
            (20, 17, 2, null, _adminUserId, "Коледен концерт", "Концерт на учениците в актовата зала."),
        };
        foreach (var e in events)
        {
            var start = now.AddDays(e.InDays).AddHours(e.Hour);
            _db.SchoolEvents.Add(new SchoolEvent { Title = e.Title, Description = e.Description, StartDateTime = start, EndDateTime = start.AddHours(e.Hours), TargetClassId = e.Target, CreatedByUserId = e.Creator });
        }
    }

    private void CreateNotifications()
    {
        foreach (var student in _db.Students)
        {
            var userId = _db.Users.First(u => u.StudentId == student.Id).Id;
            foreach (var grade in _db.Grades.Where(g => g.StudentId == student.Id).OrderByDescending(g => g.Date).Take(2))
            {
                var subject = _db.Subjects.First(s => s.Id == grade.SubjectId).Name;
                Notify(userId, NotificationType.NewGrade, "Нова оценка", $"{subject}: {BulgarianNames.Grade(grade.Value)} ({BulgarianNames.Of(grade.GradeType)})", grade.Id, grade.CreatedAtUtc);
            }

            foreach (var hw in _db.Homework.Where(h => h.ClassId == student.ClassId && h.IsActive && h.DueDate >= _anchor))
            {
                Notify(userId, NotificationType.NewHomework, "Ново домашно", $"{hw.Title} (срок {hw.DueDate:dd.MM.yyyy})", hw.Id, hw.CreatedAtUtc);
            }
        }
    }

    private void Notify(Guid userId, NotificationType type, string title, string message, Guid relatedId, DateTime at)
        => _db.Notifications.Add(new Notification { RecipientUserId = userId, Type = type, Title = title, Message = message, RelatedEntityId = relatedId, CreatedAtUtc = at });

    private User AddUser(string username, string displayName, string password, UserRole role, Guid? studentId, Guid? teacherId)
    {
        var (hash, salt) = _hasher.Hash(password);
        var user = new User
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = role,
            StudentId = studentId,
            TeacherId = teacherId,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.Users.Add(user);
        return user;
    }

    private AttendanceStatus RandomAttendance() => _rng.NextDouble() switch
    {
        < 0.92 => AttendanceStatus.Present,
        < 0.95 => AttendanceStatus.Absent,
        < 0.98 => AttendanceStatus.ExcusedAbsent,
        _ => AttendanceStatus.Late,
    };

    private GradeType RandomGradeType() => _rng.NextDouble() switch
    {
        < 0.35 => GradeType.Oral,
        < 0.55 => GradeType.WrittenTest,
        < 0.75 => GradeType.Test,
        < 0.90 => GradeType.Homework,
        _ => GradeType.Project,
    };

    private static DateTime ToUtc(DateOnly date, int hour) => DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
