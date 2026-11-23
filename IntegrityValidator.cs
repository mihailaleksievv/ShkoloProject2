// Owner: Participant 3 – Data Developer
using Shkolo.Domain;
using Shkolo.Domain.Enums;

namespace Shkolo.Infrastructure.Persistence;

/// <summary>Проверява дали данните от JSON са цялостни, например няма оценка към несъществуващ ученик.</summary>
public sealed class IntegrityValidator
{
    public IReadOnlyList<string> Validate(SchoolDatabase db)
    {
        var issues = new List<string>();
        var users = db.Users.Select(x => x.Id).ToHashSet();
        var students = db.Students.Select(x => x.Id).ToHashSet();
        var teachers = db.Teachers.Select(x => x.Id).ToHashSet();
        var classes = db.Classes.Select(x => x.Id).ToHashSet();
        var subjects = db.Subjects.Select(x => x.Id).ToHashSet();
        var years = db.AcademicYears.Select(x => x.Id).ToHashSet();
        var lessons = db.Lessons.Select(x => x.Id).ToHashSet();
        var timetable = db.Timetable.Select(x => x.Id).ToHashSet();
        var homework = db.Homework.Select(x => x.Id).ToHashSet();
        var badges = db.AchievementBadges.Select(x => x.Id).ToHashSet();

        void Ref(string entity, Guid id, string field, Guid target, HashSet<Guid> set)
        {
            if (!set.Contains(target))
            {
                issues.Add($"{entity} {id}: полето {field} сочи към несъществуващ запис ({target}).");
            }
        }

        void OptRef(string entity, Guid id, string field, Guid? target, HashSet<Guid> set)
        {
            if (target.HasValue)
            {
                Ref(entity, id, field, target.Value, set);
            }
        }

        void Unique<T, TKey>(string entity, IEnumerable<T> items, Func<T, TKey> key, string description)
        {
            foreach (var group in items.GroupBy(key).Where(g => g.Count() > 1))
            {
                issues.Add($"{entity}: повтарящ се {description} „{group.Key}“ ({group.Count()} записа).");
            }
        }

        foreach (var u in db.Users)
        {
            var ok = u.Role switch
            {
                UserRole.Student => u.StudentId.HasValue && u.TeacherId is null,
                UserRole.Teacher => u.TeacherId.HasValue && u.StudentId is null,
                _ => u.StudentId is null && u.TeacherId is null,
            };
            if (!ok)
            {
                issues.Add($"User {u.Id}: профилът не съответства на ролята {u.Role}.");
            }

            OptRef("User", u.Id, "StudentId", u.StudentId, students);
            OptRef("User", u.Id, "TeacherId", u.TeacherId, teachers);
        }

        Unique("User", db.Users, u => u.Username.ToUpperInvariant(), "потребител");
        Unique("Student", db.Students, s => s.StudentNumber, "ученически номер");
        Unique("Teacher", db.Teachers, t => t.EmployeeNumber, "служебен номер");
        Unique("SchoolClass", db.Classes, c => $"{c.GradeLevel}{c.Section.ToUpperInvariant()} / {c.AcademicYearId}", "клас");
        Unique("Subject", db.Subjects, s => s.Name.ToUpperInvariant(), "предмет");
        Unique("TeacherAssignment", db.TeacherAssignments, a => (a.TeacherId, a.ClassId, a.SubjectId), "назначение");
        Unique("TermGrade", db.TermGrades, t => (t.StudentId, t.SubjectId, t.AcademicYearId, t.Period), "срочна оценка");
        Unique("AttendanceRecord", db.AttendanceRecords, a => (a.StudentId, a.LessonId), "запис за присъствие");
        Unique("HomeworkSubmission", db.HomeworkSubmissions, s => (s.HomeworkId, s.StudentId), "предаване");

        if (db.AcademicYears.Count(y => y.IsActive) > 1)
        {
            issues.Add("AcademicYear: има повече от една активна учебна година.");
        }

        foreach (var s in db.Students)
        {
            Ref("Student", s.Id, "ClassId", s.ClassId, classes);
        }

        foreach (var c in db.Classes)
        {
            Ref("SchoolClass", c.Id, "AcademicYearId", c.AcademicYearId, years);
            OptRef("SchoolClass", c.Id, "ClassTeacherId", c.ClassTeacherId, teachers);
        }

        foreach (var a in db.TeacherAssignments)
        {
            Ref("TeacherAssignment", a.Id, "TeacherId", a.TeacherId, teachers);
            Ref("TeacherAssignment", a.Id, "ClassId", a.ClassId, classes);
            Ref("TeacherAssignment", a.Id, "SubjectId", a.SubjectId, subjects);
        }

        var slots = db.BellSchedule.Select(s => s.LessonNumber).ToHashSet();
        foreach (var t in db.Timetable)
        {
            Ref("TimetableEntry", t.Id, "ClassId", t.ClassId, classes);
            Ref("TimetableEntry", t.Id, "SubjectId", t.SubjectId, subjects);
            Ref("TimetableEntry", t.Id, "TeacherId", t.TeacherId, teachers);
            if (!slots.Contains(t.LessonNumber))
            {
                issues.Add($"TimetableEntry {t.Id}: час №{t.LessonNumber} липсва в звънеца.");
            }
        }

        foreach (var l in db.Lessons)
        {
            Ref("Lesson", l.Id, "ClassId", l.ClassId, classes);
            Ref("Lesson", l.Id, "SubjectId", l.SubjectId, subjects);
            Ref("Lesson", l.Id, "TeacherId", l.TeacherId, teachers);
            OptRef("Lesson", l.Id, "TimetableEntryId", l.TimetableEntryId, timetable);
        }

        foreach (var g in db.Grades)
        {
            Ref("Grade", g.Id, "StudentId", g.StudentId, students);
            Ref("Grade", g.Id, "ClassId", g.ClassId, classes);
            Ref("Grade", g.Id, "SubjectId", g.SubjectId, subjects);
            Ref("Grade", g.Id, "TeacherId", g.TeacherId, teachers);
            OptRef("Grade", g.Id, "LessonId", g.LessonId, lessons);
            if (!DomainRules.IsValidGrade(g.Value))
            {
                issues.Add($"Grade {g.Id}: невалидна стойност {g.Value}.");
            }
        }

        foreach (var t in db.TermGrades)
        {
            Ref("TermGrade", t.Id, "StudentId", t.StudentId, students);
            Ref("TermGrade", t.Id, "ClassId", t.ClassId, classes);
            Ref("TermGrade", t.Id, "SubjectId", t.SubjectId, subjects);
            Ref("TermGrade", t.Id, "AcademicYearId", t.AcademicYearId, years);
            Ref("TermGrade", t.Id, "TeacherId", t.TeacherId, teachers);
            if (!DomainRules.IsValidTermGrade(t.Value))
            {
                issues.Add($"TermGrade {t.Id}: невалидна стойност {t.Value}.");
            }
        }

        foreach (var a in db.AttendanceRecords)
        {
            Ref("AttendanceRecord", a.Id, "StudentId", a.StudentId, students);
            Ref("AttendanceRecord", a.Id, "LessonId", a.LessonId, lessons);
            Ref("AttendanceRecord", a.Id, "RecordedByTeacherId", a.RecordedByTeacherId, teachers);
        }

        foreach (var h in db.Homework)
        {
            Ref("Homework", h.Id, "ClassId", h.ClassId, classes);
            Ref("Homework", h.Id, "SubjectId", h.SubjectId, subjects);
            Ref("Homework", h.Id, "TeacherId", h.TeacherId, teachers);
        }

        foreach (var s in db.HomeworkSubmissions)
        {
            Ref("HomeworkSubmission", s.Id, "HomeworkId", s.HomeworkId, homework);
            Ref("HomeworkSubmission", s.Id, "StudentId", s.StudentId, students);
        }

        foreach (var f in db.Feedback)
        {
            Ref("Feedback", f.Id, "StudentId", f.StudentId, students);
            Ref("Feedback", f.Id, "TeacherId", f.TeacherId, teachers);
        }

        foreach (var a in db.Announcements)
        {
            Ref("Announcement", a.Id, "AuthorUserId", a.AuthorUserId, users);
            OptRef("Announcement", a.Id, "TargetClassId", a.TargetClassId, classes);
        }

        foreach (var e in db.SchoolEvents)
        {
            Ref("SchoolEvent", e.Id, "CreatedByUserId", e.CreatedByUserId, users);
            OptRef("SchoolEvent", e.Id, "TargetClassId", e.TargetClassId, classes);
        }

        foreach (var a in db.StudentAchievements)
        {
            Ref("StudentAchievement", a.Id, "BadgeId", a.BadgeId, badges);
            Ref("StudentAchievement", a.Id, "StudentId", a.StudentId, students);
            Ref("StudentAchievement", a.Id, "AssignedByTeacherId", a.AssignedByTeacherId, teachers);
        }

        foreach (var n in db.Notifications)
        {
            Ref("Notification", n.Id, "RecipientUserId", n.RecipientUserId, users);
        }

        return issues;
    }
}
