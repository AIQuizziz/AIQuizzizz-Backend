namespace CleanArchitectureBase.Domain.Entities;

public enum QuestionType
{
    MultipleChoice,
    Matching,
    Ordering,
    ShortText,
    /// <summary>Thẻ ghi nhớ ngôn ngữ: mặt trước = QuestionText, mặt sau = DataJson (QTypeLanguageFlashcard). Khi học/kiểm tra được dựng thành trắc nghiệm.</summary>
    LanguageFlashcard
}

public enum TextFormat
{
    // Giữ nguyên thứ tự/giá trị số. Giá trị lưu DB là chuỗi ("Markdown"); dòng cũ "MarkDown" vẫn đọc được (parse không phân biệt hoa thường).
    Markdown,
    PlainText,
    Html
}

public enum QuestionSetVisibilityMode
{
    Public = 0, //Các số có thể dùng để đẩy vào priority nếu cần
    Private = 1,
    // OnlyClass = 2
}

public enum QuestionSetUserShareMode
{
    Owner,
    Editable,
    ViewOnly
}

public enum FolderShareMode
{
    Owner,
    Editable,
    ViewOnly
}

public enum TestTemplateUserShareMode
{
    Owner,
    Editable,
    ViewOnly
}

//====Bắt đầu JSON
public class QTypeMultipleChoice
{
    public required Guid Id { get; set; }
    // public required string QuestionId { get; set; }
    public required string Text { get; set; }
    public bool IsAnswer { get; set; }
    public short ShuffleOrder { get; set; }
    /// <summary>Vị trí do tác giả sắp xếp (index trong mảng gửi lên). Dữ liệu cũ = 0 hết nên fallback theo thứ tự mảng.</summary>
    public int Position { get; set; }
}

public class QTypeMatching
{
    public required Guid Id { get; set; }
    // public required string QuestionId { get; set; }
    public required string Text { get; set; }
    public string? AnswerId { get; set; }
    public short ShuffleOrder { get; set; }
    /// <summary>Vị trí cặp do tác giả sắp xếp (cả vế trái và vế phải của một cặp có cùng Position).</summary>
    public int Position { get; set; }
}

public class QTypeOrderingItem
{
    public required Guid Id { get; set; }
    // public required string QuestionId { get; set; }
    public required string Text { get; set; }
    public required int CorrectOrder { get; set; }
    public short ShuffleOrder { get; set; }
    /// <summary>Vị trí do tác giả sắp xếp (khác với CorrectOrder).</summary>
    public int Position { get; set; }
}

public class QTypeShortAnswer
{
    public required string Answer { get; set; }
}

/// <summary>
/// DataJson của QuestionType.LanguageFlashcard (v = 1). Mặt trước là Question.QuestionText.
/// Đáp án nhiễu của câu trắc nghiệm KHÔNG lưu ở đây - được dựng lúc học/kiểm tra từ các thẻ khác trong bộ.
/// </summary>
public class QTypeLanguageFlashcard
{
    [System.Text.Json.Serialization.JsonPropertyName("v")] public int V { get; set; } = 1;
    [System.Text.Json.Serialization.JsonPropertyName("back")] public QFlashcardBack Back { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("lang")] public QFlashcardLang? Lang { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("source")] public QFlashcardSource? Source { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("lookup")] public QFlashcardLookup? Lookup { get; set; }
}

public class QFlashcardBack
{
    [System.Text.Json.Serialization.JsonPropertyName("meanings")] public List<QFlashcardMeaning> Meanings { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("phonetic")] public string? Phonetic { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("audioUrl")] public string? AudioUrl { get; set; }
}

public class QFlashcardMeaning
{
    /// <summary>Từ loại (noun, verb, adjective...)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("pos")] public string? Pos { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("definition")] public string Definition { get; set; } = string.Empty;
    /// <summary>Nghĩa ở ngôn ngữ đích (khi bật dịch)</summary>
    [System.Text.Json.Serialization.JsonPropertyName("translation")] public string? Translation { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("examples")] public List<string> Examples { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("synonyms")] public List<string> Synonyms { get; set; } = new();
}

public class QFlashcardLang
{
    [System.Text.Json.Serialization.JsonPropertyName("from")] public string? From { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("to")] public string? To { get; set; }
}

public class QFlashcardSource
{
    [System.Text.Json.Serialization.JsonPropertyName("url")] public string? Url { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("title")] public string? Title { get; set; }
    /// <summary>Câu chứa từ được bôi đen</summary>
    [System.Text.Json.Serialization.JsonPropertyName("context")] public string? Context { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("capturedAt")] public DateTime? CapturedAt { get; set; }
}

public class QFlashcardLookup
{
    [System.Text.Json.Serialization.JsonPropertyName("provider")] public string? Provider { get; set; }
    /// <summary>found | not_found | skipped</summary>
    [System.Text.Json.Serialization.JsonPropertyName("status")] public string? Status { get; set; }
}

//====Kết thúc JSON

public class Question : BaseAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? QuestionSetId { get; set; }
    public required QuestionType Type { get; set; }
    public string? QuestionText { get; set; }
    public TextFormat TextFormat { get; set; } = TextFormat.Html;
    public string? ExplainText { get; set; }
    public float Score { get; set; }
    /// <summary>Vị trí câu hỏi trong bộ câu hỏi / template (0-based)</summary>
    public int Order { get; set; }
    public string? DataJson { get; set; } //Lưu JSON List<QTypeOrderingItem>/List<QTypeMatching>/List<QTypeMultipleChoice>
    public bool IsDeleted { get; set; }
    public object? Data { get; set; } //Không Map

    // Media đính kèm (1 ảnh hoặc 1 video). MediaType lưu kèm để build URL mà không cần Include Media
    public Guid? MediaId { get; set; }
    public MediaType? MediaType { get; set; }


    // Navigation properties
    public Media? Media { get; set; }
    public QuestionSet? QuestionSet { get; set; }
    public List<Comment> Comments { get; set; } = new();
    public List<UserQuestionSetHistory> UserQuestionSetHistories { get; set; } = new();
    public List<TestTemplateQuestion> TestTemplateQuestions { get; set; } = new();
    public List<TestVersionQuestion> TestVersionQuestions { get; set; } = new();
    public List<AttemptQuestion> AttemptQuestions { get; set; } = new();
}

public class Tag : BaseEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public int QuestionSetCount { get; set; }

    // Navigation properties
    public List<QuestionSetTag> QuestionSetTags { get; set; } = new();
}

public class QuestionSetTag
{
    public required Guid TagId { get; set; }
    public required Guid QuestionSetId { get; set; }

    public Tag? Tag { get; set; }
    public QuestionSet? QuestionSet { get; set; }
}

public class QuestionSet : BaseAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public QuestionSetVisibilityMode VisibilityMode { get; set; }
    public int QuestionCount { get; set; }
    public List<Question> Questions { get; set; } = new();
    public bool IsDeleted { get; set; }

    public int RatingCount { get; set; }
    public int RatingSum { get; set; }
    public double RatingAverage { get; set; }

    // Navigation properties
    public List<QuestionSetUser> QuestionSetUsers { get; set; } = new();
    public List<QuestionSetTag> QuestionSetTags { get; set; } = new();
    public List<ClassQuestionSet> ClassQuestionSets { get; set; } = new();
    public List<UserQuestionSetAccessHistory> AccessHistories { get; set; } = new();
    public List<QuestionSetRating>  QuestionSetRatings { get; set; } = new();
}

public class QuestionSetUser : BaseAuditableEntity
{
    // public required Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required Guid QuestionSetId { get; set; }
    public QuestionSetUserShareMode ShareMode { get; set; }

    public User? User { get; set; }
    public QuestionSet? QuestionSet { get; set; }
}

public class QuestionSetRating : BaseAuditableEntity
{
    public required Guid QuestionSetId { get; set; }
    public int Rating { get; set; }
    public QuestionSet? QuestionSet { get; set; }
}

public class Comment : BaseAuditableEntity
{
    public required Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public required Guid QuestionId { get; set; }
    public required string Content { get; set; }
    public bool IsDeleted { get; set; }

    public Question? Question { get; set; }
    // Nếu ParentId là Comment cha (recursive relationship)
    public Comment? ParentComment { get; set; }
    public List<Comment> ChildComments { get; set; } = new();
}

public class UserQuestionSetHistory : BaseAuditableEntity
{
    public required Guid UserId { get; set; }
    public required Guid QuestionId { get; set; }
    public required bool IsCorrect { get; set; }

    public User? User { get; set; }
    public Question? Question { get; set; }
}

public class UserQuestionSetAccessHistory : BaseEntity
{
    public required Guid UserId { get; set; }
    public required Guid QuestionSetId { get; set; }

    public DateTimeOffset LastAccess { get; set; } = DateTimeOffset.UtcNow;
    public bool IsSaved { get; set; }
    public User? User { get; set; }
    public QuestionSet? QuestionSet { get; set; }
}

public class TestTemplate : BaseAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
    public string? Description { get; set; }

    // Navigation properties
    public List<TestTemplateQuestion> TestTemplateQuestions { get; set; } = new();
    public List<TestTemplateUser> TestTemplateUsers { get; set; } = new();
    public List<FolderTestTemplate> FolderTestTemplates { get; set; } = new();
}

public class TestTemplateQuestion : BaseAuditableEntity
{
    public  Guid Id { get; set; } = Guid.NewGuid();
    public required Guid TestTemplateId { get; set; }
    public required Guid QuestionId { get; set; }

    public TestTemplate? TestTemplate { get; set; }
    public Question? Question { get; set; }
}

public class TestTemplateUser : BaseAuditableEntity
{
    public required Guid UserId { get; set; }
    public required Guid TestTemplateId { get; set; }
    public TestTemplateUserShareMode ShareMode { get; set; }
    public bool IsViewed { get; set; }
    public User? User { get; set; }
    public TestTemplate? TestTemplate { get; set; }
}

public class Folder : BaseAuditableEntity
{
    public  Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }

    // Navigation properties
    public List<FolderUser> FolderUsers { get; set; } = new();
    public List<FolderTestTemplate> FolderTestTemplates { get; set; } = new();
}

public class FolderTestTemplate : BaseAuditableEntity
{
    public required Guid FolderId { get; set; }
    public required Guid TestTemplateId { get; set; }

    public Folder? Folder { get; set; }
    public TestTemplate? TestTemplate { get; set; }
}

public class FolderUser : BaseAuditableEntity
{
    public required Guid UserId { get; set; }
    public required Guid FolderId { get; set; }
    public FolderShareMode ShareMode { get; set; }
    public bool IsViewed { get; set; }
    public User? User { get; set; }
    public Folder? Folder { get; set; } // Giả định FolderId tồn tại
}

public enum GradeAttemptMethod
{
    LastAttempt,
    HighestScore,
}

public enum GradeQuestionMethod
{
    Partial,
    AllOrNothing
}

public enum TestStatus
{
    Active,
    Completed,
    Upcoming
}

public class Test : BaseAuditableEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required Guid ClassId { get; set; }
    public required DateTimeOffset TimeStart { get; set; }
    public required DateTimeOffset TimeFinish { get; set; }
    public int MaxAttempt { get; set; }
    public required int TimeLimit { get; set; }
    public required int QuestionCount { get; set; }
    public float PassingScore { get; set; }
    public required GradeAttemptMethod GradeAttemptMethod { get; set; }
    public required GradeQuestionMethod GradeQuestionMethod { get; set; }
    public bool IsShowCorrectAnswerInReview { get; set; }
    public bool IsAllowReviewAfterSubmit { get; set; }

    public bool IsDeleted { get; set; }

    public List<TestGrade> TestGrades { get; set; } = new();
    public List<TestVersion> TestVersions { get; set; } = new();
    public List<Attempt> Attempts { get; set; } = new();
    public Class? Class { get; set; }
}

public class TestVersion : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid TestId { get; set; }
    public required int No { get; set; }

    public List<TestVersionQuestion> TestVersionQuestions { get; set; } = new();
    public List<Attempt> Attempts { get; set; } = new();
    public Test? Test { get; set; }
}

public class TestVersionQuestion : BaseEntity
{
    public required Guid Id { get; set; }
    public required Guid TestVersionId { get; set; }
    public required Guid QuestionId { get; set; }
    public required int Order { get; set; }

    public TestVersion? TestVersion { get; set; }
    public Question? Question { get; set; }
}

//TODO: Thêm grademode vào
public class TestGrade
{
    public required Guid Id { get; set; }
    public required Guid TestId { get; set; }
    public required Guid UserId { get; set; }
    public required float Score { get; set; }

    public Test? Test { get; set; }
    public User? User { get; set; }
}

public enum AttemptStatus
{
    Passed,
    Failed,
}

public class Attempt
{
    public required Guid Id { get; set; }
    public required Guid TestId { get; set; }
    public required Guid TestVersionId { get; set; }
    public required Guid UserId { get; set; }
    public required DateTimeOffset TimeStart { get; set; }
    public required DateTimeOffset TimeFinish { get; set; }
    public required float Score { get; set; }

    public List<AttemptQuestion> AttemptQuestions { get; set; } = new();
    public Test? Test { get; set; }
    public TestVersion? TestVersion { get; set; }
    public User? User { get; set; }
}

public class AttemptQuestion
{
    public required Guid Id { get; set; }
    public required Guid AttemptId { get; set; }
    public required Guid QuestionId { get; set; }
    public int Order { get; set; }
    public float Score { get; set; }
    public required string DataJson { get; set; } //Lưu JSON List<QTypeOrderingItemAnswer>/List<QTypeMatchingAnswer>/List<QTypeMultipleChoiceAnswer>
    public object? Data { get; set; } //Không Map

    public Attempt? Attempt { get; set; }
    public Question? Question { get; set; }
}

//====Bắt đầu JSON
public class QTypeMultipleChoiceAnswer
{
    public required Guid ChoiceId { get; set; }
}

public class QTypeOrderingAnswer
{
    public required Guid ItemId { get; set; }
    public required int Order { get; set; }
}

public class QTypeMatchingAnswer
{
    public required Guid RightItemId { get; set; }
    public required Guid LeftItemId { get; set; }
}
//====Kết thúc JSON
