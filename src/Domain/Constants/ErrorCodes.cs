// ReSharper disable InconsistentNaming
namespace CleanArchitectureBase.Domain.Constants;

public static class ErrorCodes
{
    //COMMON
    public const string COMMON_FORBIDDEN = nameof(COMMON_FORBIDDEN);
    public const string COMMON_INVALID_MODEL = nameof(COMMON_INVALID_MODEL);
    public const string COMMON_UNAUTHORIZED = nameof(COMMON_UNAUTHORIZED);  
    public const string COMMON_SERVER_INTERNAL_ERROR = nameof(COMMON_SERVER_INTERNAL_ERROR);
    public const string COMMON_NOT_FOUND = nameof(COMMON_NOT_FOUND);
    public const string FIELD_NAME_NOT_FOUND = nameof(FIELD_NAME_NOT_FOUND);

    //ACCOUNT
    public const string ACCOUNT_NOTFOUND = nameof(ACCOUNT_NOTFOUND);
    public const string ACCOUNT_LOCKED_OUT = nameof(ACCOUNT_LOCKED_OUT);
    public const string ACCOUNT_BANNED = nameof(ACCOUNT_BANNED);
    public const string ACCOUNT_INVALID_CREDENTIALS = nameof(ACCOUNT_INVALID_CREDENTIALS);
    public const string ACCOUNT_INVALID_VERIFICATION_CODE = nameof(ACCOUNT_INVALID_VERIFICATION_CODE);
    public const string ACCOUNT_EMAIL_NOT_VERIFIED = nameof(ACCOUNT_EMAIL_NOT_VERIFIED); 
    public const string ACCOUNT_EMAIL_BANNED  = nameof(ACCOUNT_EMAIL_BANNED ); 
    public const string ACCOUNT_INVALID_RESET_CODE  = nameof(ACCOUNT_INVALID_RESET_CODE ); 
    public const string ACCOUNT_WRONG_PASSWORD  = nameof(ACCOUNT_WRONG_PASSWORD );
    public const string ACCOUNT_NEW_PASSWORD_SAME_AS_CURRENT = nameof(ACCOUNT_NEW_PASSWORD_SAME_AS_CURRENT);
    public const string EMAIL_VERIFICATION_REQUEST_TOO_MANY  = nameof(EMAIL_VERIFICATION_REQUEST_TOO_MANY ); 
    public const string EMAIL_VERIFICATION_CODE_FAILED_TOO_MANY  = nameof(EMAIL_VERIFICATION_CODE_FAILED_TOO_MANY ); 
    public const string PASSWORD_RESET_REQUEST_TOO_MANY  = nameof(PASSWORD_RESET_REQUEST_TOO_MANY ); 
    public const string PASSWORD_RESET_CODE_FAILED_TOO_MANY  = nameof(PASSWORD_RESET_CODE_FAILED_TOO_MANY ); 
    public const string REFRESHTOKEN_NOTFOUND  = nameof(REFRESHTOKEN_NOTFOUND ); 
    
    
    //ROLE
    public const string ROLE_NOTFOUND  = nameof(ROLE_NOTFOUND ); 
    
    //USER
    public const string USER_NOTFOUND  = nameof(USER_NOTFOUND );
    
    //PAYMENT
    public const string PAYMENT_TRANSACTION_EXISTED  = nameof(PAYMENT_TRANSACTION_EXISTED );
    
    
    //IDENTITY OVERRIDE ERROR DESCRIBER
    public const string IDENTITY_DEFAULT_ERROR = nameof(IDENTITY_DEFAULT_ERROR);
    public const string IDENTITY_CONCURRENCY_FAILURE = nameof(IDENTITY_CONCURRENCY_FAILURE);
    public const string IDENTITY_PASSWORD_MISMATCH = nameof(IDENTITY_PASSWORD_MISMATCH);
    public const string IDENTITY_INVALID_TOKEN = nameof(IDENTITY_INVALID_TOKEN);
    public const string IDENTITY_RECOVERY_CODE_REDEMPTION_FAILED = nameof(IDENTITY_RECOVERY_CODE_REDEMPTION_FAILED);
    public const string IDENTITY_LOGIN_ALREADY_ASSOCIATED = nameof(IDENTITY_LOGIN_ALREADY_ASSOCIATED);
    public const string IDENTITY_INVALID_USER_NAME = nameof(IDENTITY_INVALID_USER_NAME);
    public const string IDENTITY_INVALID_EMAIL = nameof(IDENTITY_INVALID_EMAIL);
    public const string IDENTITY_DUPLICATE_USER_NAME = nameof(IDENTITY_DUPLICATE_USER_NAME);
    public const string IDENTITY_DUPLICATE_EMAIL = nameof(IDENTITY_DUPLICATE_EMAIL);
    public const string IDENTITY_INVALID_ROLE_NAME = nameof(IDENTITY_INVALID_ROLE_NAME);
    public const string IDENTITY_DUPLICATE_ROLE_NAME = nameof(IDENTITY_DUPLICATE_ROLE_NAME);
    public const string IDENTITY_USER_ALREADY_HAS_PASSWORD = nameof(IDENTITY_USER_ALREADY_HAS_PASSWORD);
    public const string IDENTITY_USER_LOCKOUT_NOT_ENABLED = nameof(IDENTITY_USER_LOCKOUT_NOT_ENABLED);
    public const string IDENTITY_USER_ALREADY_IN_ROLE = nameof(IDENTITY_USER_ALREADY_IN_ROLE);
    public const string IDENTITY_USER_NOT_IN_ROLE = nameof(IDENTITY_USER_NOT_IN_ROLE);
    public const string IDENTITY_PASSWORD_TOO_SHORT = nameof(IDENTITY_PASSWORD_TOO_SHORT);
    public const string IDENTITY_PASSWORD_REQUIRES_UNIQUE_CHARS = nameof(IDENTITY_PASSWORD_REQUIRES_UNIQUE_CHARS);
    public const string IDENTITY_PASSWORD_REQUIRES_NON_ALPHANUMERIC = nameof(IDENTITY_PASSWORD_REQUIRES_NON_ALPHANUMERIC);
    public const string IDENTITY_PASSWORD_REQUIRES_DIGIT = nameof(IDENTITY_PASSWORD_REQUIRES_DIGIT);
    public const string IDENTITY_PASSWORD_REQUIRES_LOWER = nameof(IDENTITY_PASSWORD_REQUIRES_LOWER);
    public const string IDENTITY_PASSWORD_REQUIRES_UPPER = nameof(IDENTITY_PASSWORD_REQUIRES_UPPER);
    
    //Class
    public const string CLASS_NOTFOUND = nameof(CLASS_NOTFOUND);
    public const string PERMISSION_NOT_FOUND = nameof(PERMISSION_NOT_FOUND);
    public const string CLASS_CODE_NOT_FOUND = nameof(CLASS_CODE_NOT_FOUND);
    public const string STUDENT_ALREADY_EXISTS_IN_CLASS = nameof(STUDENT_ALREADY_EXISTS_IN_CLASS);
    public const string ONLY_OWNERS_CAN_UPDATE = nameof(ONLY_OWNERS_CAN_UPDATE);
    public const string NOT_FOUND_STUDENT_IN_CLASS = nameof(NOT_FOUND_STUDENT_IN_CLASS);
    public const string NOT_FOUND_TEACHER_OR_OWNER_IN_CLASS = nameof(NOT_FOUND_TEACHER_OR_OWNER_IN_CLASS);
    public const string NOT_FOUND_USER_IN_CLASS = nameof(NOT_FOUND_USER_IN_CLASS);
    public const string QUESTION_SET_ALREADY_IN_CLASS = nameof(QUESTION_SET_ALREADY_IN_CLASS);
    public const string NOT_HAVE_PERMISSION_TO_ADD_QUESTION_SET = nameof(NOT_HAVE_PERMISSION_TO_ADD_QUESTION_SET);
    public const string OWNER_CAN_NOT_MOVE_OUT_CLASS = nameof(OWNER_CAN_NOT_MOVE_OUT_CLASS);
    
    //Question
    public const string INVALID_QUESTION_TYPE = nameof(INVALID_QUESTION_TYPE);
    public const string QUESTION_NOT_FOUND_TO_DELETE = nameof(QUESTION_NOT_FOUND_TO_DELETE);
    
    //Question set
    public const string QUESTION_SET_NOT_FOUND = nameof(QUESTION_SET_NOT_FOUND);
    public const string QUESTION_SET_NOT_FOUND_IN_CLASS = nameof(QUESTION_SET_NOT_FOUND_IN_CLASS);
    public const string USER_NOT_ACCESS_TO_QUESTION_SET = nameof(USER_NOT_ACCESS_TO_QUESTION_SET);
    public const string PLAN_REQUIRE_PLAN = nameof(PLAN_REQUIRE_PLAN);
    
    //Folder
    public const string FOLDER_NOT_FOUND = nameof(FOLDER_NOT_FOUND);
    public const string FOLDER_ALREADY_EXISTS = nameof(FOLDER_ALREADY_EXISTS);
    public const string USER_NOT_HAVE_PERMISSION_IN_FOLDER = nameof(USER_NOT_HAVE_PERMISSION_IN_FOLDER);
    public const string QUESTION_SET_ALREADY_EXISTS_IN_FOLDER = nameof(QUESTION_SET_ALREADY_EXISTS_IN_FOLDER);
    public const string QUESTION_SET_NOT_FOUND_IN_FOLDER = nameof(QUESTION_SET_NOT_FOUND_IN_FOLDER);

    
    //Test
    public const string MAX_ATTEMPT_IN_THIS_TEST = nameof(MAX_ATTEMPT_IN_THIS_TEST);
    public const string TEST_IS_OVERDUE = nameof(TEST_IS_OVERDUE);
    public const string TEST_NOT_FOUND = nameof(TEST_NOT_FOUND);
    public const string NUMBER_OF_QUESTION_EXCEED_LIMIT = nameof(NUMBER_OF_QUESTION_EXCEED_LIMIT);
    public const string USER_NOT_HAVE_PERMISSION_IN_TEST = nameof(USER_NOT_HAVE_PERMISSION_IN_TEST);
    public const string NOT_YET_TIME_TO_OPEN_TEST = nameof(NOT_YET_TIME_TO_OPEN_TEST);
    public const string STUDENT_CAN_REVIEW_THIS_TEST = nameof(STUDENT_CAN_REVIEW_THIS_TEST);
    public const string TEST_ALREADY_OPEN = nameof(TEST_ALREADY_OPEN);
    /// <summary>Câu hỏi trong bài test không có câu gốc (hoặc câu gốc đã bị xoá) để cập nhật theo</summary>
    public const string SOURCE_QUESTION_NOT_FOUND = nameof(SOURCE_QUESTION_NOT_FOUND);

    //File
    public const string FILE_NOT_FOUND = nameof(FILE_NOT_FOUND);
    public const string INVALID_FILE_FORMAT = nameof(INVALID_FILE_FORMAT);
    public const string ERROR_FORMAT_FILE = nameof(ERROR_FORMAT_FILE);
    public const string FILE_EMPTY = nameof(FILE_EMPTY);
    public const string FILE_TOO_LARGE = nameof(FILE_TOO_LARGE);
    
    //Attempt
    public const string ERROR_ATTEMPT_USER = nameof(ERROR_ATTEMPT_USER);
    public const string ATTEMPT_ALREADY_SUBMIT = nameof(ATTEMPT_ALREADY_SUBMIT);
    public const string ATTEMPT_NOT_FOUND = nameof(ATTEMPT_NOT_FOUND);
    public const string NOT_SUBMITTED_CAN_NOT_VIEW = nameof(NOT_SUBMITTED_CAN_NOT_VIEW);
    
    //Comment
    public const string QUESTION_CAN_NOT_COMMENT = nameof(QUESTION_CAN_NOT_COMMENT);
    public const string USER_NOT_HAVE_PERMISSION_IN_COMMENT = nameof(USER_NOT_HAVE_PERMISSION_IN_COMMENT);
    public const string COMMENT_NOT_FOUND = nameof(COMMENT_NOT_FOUND);
    
    //Plan
    public const string USER_NOT_HAVE_PERMISSION = nameof(USER_NOT_HAVE_PERMISSION);
    public const string PLAN_NOT_FOUND = nameof(PLAN_NOT_FOUND);
    public const string INSUFFICIENT_BALANCE = nameof(INSUFFICIENT_BALANCE);
    
    //AI
    public const string FILE_UPLOAD_FAILED = nameof(FILE_UPLOAD_FAILED);
    public const string FILE_URI_NOTFOUND = nameof(FILE_URI_NOTFOUND);
    public const string GENERATE_CONTENT_FAILED = nameof(GENERATE_CONTENT_FAILED);
    public const string AI_FILE_TOO_LARGE = nameof(AI_FILE_TOO_LARGE);
    
    public const string INVALID_FILE_TYPE = nameof(INVALID_FILE_TYPE);
    public const string PDF_PAGE_LIMIT_EXCEEDED = nameof(PDF_PAGE_LIMIT_EXCEEDED);
    
    public const string NO_STRUCTURE_FOUND = nameof(NO_STRUCTURE_FOUND);
    public const string PAYMENT_IN_PROGRESS = nameof(PAYMENT_IN_PROGRESS);
    
    //System setting
    public const string SYSTEM_SETTING_NOT_FOUND = nameof(SYSTEM_SETTING_NOT_FOUND);

    //Media
    public const string PLAN_NOT_ALLOW_UPLOAD_IMAGE = nameof(PLAN_NOT_ALLOW_UPLOAD_IMAGE);
    public const string PLAN_NOT_ALLOW_UPLOAD_VIDEO = nameof(PLAN_NOT_ALLOW_UPLOAD_VIDEO);
    public const string MEDIA_INVALID_FILE = nameof(MEDIA_INVALID_FILE);
    public const string MEDIA_UNSUPPORTED_TYPE = nameof(MEDIA_UNSUPPORTED_TYPE);
    public const string MEDIA_FILE_TOO_LARGE = nameof(MEDIA_FILE_TOO_LARGE);
    public const string MEDIA_VIDEO_TOO_LONG = nameof(MEDIA_VIDEO_TOO_LONG);
    public const string MEDIA_PROCESS_FAILED = nameof(MEDIA_PROCESS_FAILED);
    public const string MEDIA_NOT_FOUND = nameof(MEDIA_NOT_FOUND);
    public const string MEDIA_VIOLATED = nameof(MEDIA_VIOLATED);
}
