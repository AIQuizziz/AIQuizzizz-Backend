# Changelog — AIQuizzizz-Backend

Định dạng theo [Keep a Changelog](https://keepachangelog.com/vi/1.1.0/). Nhánh gốc: `master` (`a0a8fe1`).

## [Unreleased] — 2026-10-19

### Fixed — Tra từ WiktAPI: chữ hoa đầu từ, thứ tự nghĩa, ví dụ, bản dịch y nguyên
- **Chữ hoa / thường**: WiktAPI phân biệt hoa/thường nên "Created" không tìm thấy (chỉ có "created"), "Junior"/"Middle" ra tên thị trấn / vùng đất. Nay chữ thường hoặc hoa chữ đầu tra cả hai dạng, lấy dạng nhiều nghĩa hơn (bằng nhau giữ chữ đã gõ): "Junior" -> "junior", "House" -> "house", "English" giữ nguyên, "london" -> "London"; dạng khác (FOMO, iPhone) thử đúng chữ gốc trước. Phần dịch dùng lại mục từ đã chọn.
- **Cache theo đúng chữ đã gõ** (trước gộp về chữ thường): kết quả sai của "Junior" từng bị dùng lại cho "junior" trong 24 giờ, "Created" (không tìm thấy) cho "created" trong 10 phút.
- **Thứ tự nghĩa**: dạng biến đổi ("simple past of run") lên đầu, nghĩa hiếm / cổ / cách viết khác xuống cuối, rồi danh từ > động từ > tính từ > trạng từ. "ran" không còn mở đầu bằng "Yarns coiled on a spun-yarn winch".
- **Ví dụ**: bỏ trích dẫn sách / báo (có `ref`, hoặc không ghi type mà mở đầu bằng năm / dài quá 200 ký tự, vd "1821-1822, Vicesimus Knox, Remarks on…"), chỉ giữ câu ví dụ ngắn.
- **Bản dịch y nguyên**: LibreTranslate trả lại đúng từ gốc ("ephemeral" -> "ephemeral") thì coi là không dịch được.
- Test: 6 test mới trong `WiktapiServiceTests`, cập nhật test cũ theo số request mới; unit 186/186.

### Changed — Tra từ / dịch từ cho thẻ ghi nhớ: WiktAPI tự host thay Free Dictionary API + MyMemory
- **`WiktapiService`** (`IDictionaryLookupService` + phần dịch từ) gọi WiktAPI tự host (github.com/TheAlexLichter/wiktapi.dev @886100d, dữ liệu Wiktionary qua kaikki.org): định nghĩa + ví dụ ngắn (`/definitions`), IPA + file phát âm Wikimedia Commons (`/pronunciations`). Dịch từ tiếng Anh: bảng `translations` của Wiktionary tiếng Anh (nghĩa đầu tiên có bản dịch, tối đa 3); từ ngôn ngữ khác -> tiếng Anh: gloss tiếng Anh. Nghĩa xếp ưu tiên danh từ > động từ > tính từ > trạng từ (Wiktionary có thể để tính từ trước, vd "nhà" -> "domestic"). Cache 24h (không có từ: 10 phút, lỗi: không cache).
- **`WordTranslationService`**: Wiktionary không có bản dịch (vd "serendipity" -> vi) thì dịch bằng LibreTranslate (cũng tự host); từ không có trong từ điển thì không gọi máy dịch (tránh "dịch" chuỗi vô nghĩa). Câu dài vẫn dịch bằng LibreTranslate như trước.
- Cấu hình `Wiktapi__BaseUrl` (+ `Wiktapi__Editions`, mặc định `en,vi`); bỏ trống thì dùng lại Free Dictionary API + MyMemory (dịch vụ ngoài, cho môi trường dev). `IDictionaryLookupService.Provider`: thẻ ghi `lookup.provider = "wiktionary"` thay cho `dictionaryapi.dev` hằng số.
- Triển khai: service `wiktapi` trong `deploy/docker-compose.yml` (chỉ nội bộ); dữ liệu tạo bằng `deploy/wiktapi-setup.sh`: Wiktionary tiếng Anh lọc còn mục từ en + vi (10,9 triệu -> 1,54 triệu dòng) + Wiktionary tiếng Việt -> SQLite 1.962.901 mục từ, 2,29 GB; container dùng ~71 MB RAM, tra 8–20 ms (lần đầu chưa cache ~200 ms).
- Test: `WiktapiServiceTests` (7, JSON lấy từ phản hồi thật). Unit 180/180 trên server. Gọi thử endpoint dịch của extension trên production: house -> "nhà, nhà ở"; serendipity -> "may mắn" (dự phòng LibreTranslate); nhà -> "house; home; dwelling"; câu -> LibreTranslate; chuỗi vô nghĩa -> unavailable.

### Changed — Bỏ mẫu đề thi; thư mục chứa bộ câu hỏi; câu hỏi có phiên bản, bài test giữ bản sao
- **Bỏ `TestTemplate`** (entity, `Application/TestTemplates`, `/api/TestTemplate`, test liên quan). Nhập câu hỏi từ Excel chuyển sang `POST /api/Test/ImportFile` (`ImportFileTestCommand`, cùng xử lý).
- **Thư mục chứa bộ câu hỏi**: bảng `FolderQuestionSets` (thay `FolderTestTemplates`); `GET /api/Folder/{id}/QuestionSets`, `POST|DELETE /api/Folder/{id}/QuestionSet/{questionSetId}`. Thêm vào thư mục cần quyền sửa bộ + Owner/Editable trong thư mục. **Thành viên thư mục xem được các bộ trong thư mục** (`CanUserViewQuestionSet` thêm nhánh thư mục) nên chọn được câu của bộ khi tạo đề.
- **Phiên bản câu hỏi**: `Question.Version` (mặc định 1), `ContentHash` (SHA-256), `SourceQuestionId` (FK tự tham chiếu, restrict, có index), `SourceVersion`. `QuestionContentInterceptor` tính hash ở mọi đường lưu; câu thuộc bộ câu hỏi đổi nội dung thì `Version + 1`. Hash dùng nội dung đã chuẩn hoá (`QuestionContent`: bỏ id đáp án / `ShuffleOrder` vì mỗi lần lưu `QuestionTypeSerializer` sinh mới; không gồm `Score`), nên lưu lại y nguyên không tăng version. Câu cũ: hash tính bằng C# lúc khởi động (`ApplicationDbContextInitialiser.BackfillQuestionContentHashAsync`, chỉ ghi cột hash, không đổi `LastModified`).
- **Tạo đề luôn chép câu**: chọn câu từ bộ câu hỏi (bộ xem được: của mình / được chia sẻ / công khai / trong lớp / trong thư mục) -> câu mới thuộc riêng bài test, ghi `SourceQuestionId` + `SourceVersion`. Sửa bộ câu hỏi không đổi đề đã giao; sửa câu trong đề (trước giờ mở bài) **sửa tại chỗ**, không đụng bộ câu hỏi. Dữ liệu cũ dùng chung (thời mẫu đề thi) vẫn tách bản sao khi sửa.
- **Cập nhật theo bản gốc**: `POST /api/Test/{testId}/Questions/{questionId}/SyncWithSource` chép nội dung mới nhất của câu gốc vào câu trong đề (giữ điểm trong đề), chỉ trước giờ mở bài; câu gốc đã xoá / hết quyền -> `SOURCE_QUESTION_NOT_FOUND`. Chi tiết bài test trả thêm `sourceQuestionId`, `sourceVersion`, `latestSourceVersion`, `sourceQuestionSetId/Name`; `QuestionResponseDto.Version`.
- Media: câu trong bài test mình quản lý giữ được ảnh/video của người khác dù bộ gốc đổi quyền / bị xoá; thêm nhánh thư mục cho media của bộ câu hỏi.
- Migration `AddQuestionVersioning`: thêm cột + `FolderQuestionSets`; **chuyển mẫu đề thi cũ thành bộ câu hỏi riêng tư** (cùng Id, chép câu, chuyển quyền chia sẻ + vị trí trong thư mục) rồi mới xoá bảng mẫu đề thi. Đã áp thử trên bản sao DB thật (1618 câu, 48 bộ, 1 thư mục): thành công.
- Test: unit `QuestionContentTests` (4); functional `FolderQuestionSetTests` (3), `QuestionVersioningTests` (5), cập nhật `TestQuestionsTests`. Chạy trên server: unit 173/173; functional 628/665 — 37 fail **trùng khớp với `master`**, không có lỗi mới. Thử giao diện thật trên staging tạm (bản sao DB, đã dỡ): thêm vào thư mục, chọn câu từ thư mục khi tạo đề, nhãn bản mới + Cập nhật theo bản gốc, gỡ khỏi thư mục.

### Changed — Bản gốc của bài test: bảng `TestQuestions` (bỏ quy ước "TestVersion No = 0")
- **Bảng mới `TestQuestions` (`TestId`, `QuestionId`, `Score` numeric(5,2), `Order`)**, unique (`TestId`, `QuestionId`), FK `Tests` cascade / `Questions` restrict. Đây là bản gốc của bài test: danh sách câu, thứ tự gốc và **điểm tối đa của câu trong bài này** (trước đây dùng `Question.Score`, nên cùng một câu không thể có điểm khác nhau giữa các bài). `TestVersions` chỉ còn là các bản trộn phát cho học sinh, luôn sinh lại từ `TestQuestions` (`TestVersionBuilder`: bản đầu tiên giữ thứ tự gốc như trước, các bản sau trộn ngẫu nhiên).
- Migration `AddTestQuestions` kèm backfill từ version 0 cũ (thứ tự đánh lại 0..n-1, câu trùng chỉ giữ lần đầu, điểm lấy từ `Question.Score`); đã chạy thử backfill trên Postgres 16 với dữ liệu mẫu.
- Đọc bản gốc thay cho version 0: chi tiết bài test (trang sửa), kết quả lớp, lịch sử làm bài, danh sách bài test của lớp (số câu), xem lại bài làm (điểm tối đa), làm bài (điểm hiển thị từng câu). **Chấm điểm** (`SubmitTestAttemptCommand`) dùng `TestQuestion.Score`.
- Sửa bài test (`UpdateTestCommand`): chỉ đổi điểm / thứ tự thì giữ nguyên câu hỏi và cập nhật `TestQuestions` (trước đây đổi riêng điểm bị bỏ qua vì phép so sánh câu không xét điểm); câu mới hoặc đổi nội dung vẫn tạo câu mới; các bản trộn sinh lại toàn bộ (trước đây câu mới chỉ được nối vào cuối các bản trộn). Có bài làm thì không cho sửa (phòng thủ, bình thường đã chặn bởi giờ mở bài). Tạo/sửa từ chối chọn trùng một câu (`TestQuestionRules`). Chọn câu từ mẫu đề thi chỉ so nội dung, không so điểm.
- Kiểm duyệt: gỡ câu vi phạm khỏi `TestQuestions` (và các bản trộn), số câu bài test tính theo bản gốc.
- Test: unit `TestVersionBuilderTests` (3); functional `TestQuestionsTests` (5: tạo, đổi điểm, đổi nội dung + đảo thứ tự + xoá + đổi số bản trộn, chặn trùng, chấm điểm theo bản gốc). Chạy trên server (container SDK 8 + Testcontainers): unit 169/169; nhóm Tests 124/135 — 11 test fail **giống hệt trên `master` trước khi sửa** (start attempt bằng tài khoản owner, lịch kiểm tra, vài validator), không phát sinh lỗi mới.

### Added — Thẻ ghi nhớ ngôn ngữ (`LanguageFlashcard`) và API cho extension trình duyệt
- `QuestionType.LanguageFlashcard` (enum lưu dạng chuỗi nên cột `Type` không cần migration). Mặt trước = `QuestionText`; mặt sau = `DataJson` (`QTypeLanguageFlashcard`, `v`=1: `back.meanings[]` {pos, definition, translation, examples, synonyms}, `phonetic`, `audioUrl`, `lang`, `source` {url, title, context, capturedAt}, `lookup`). Giới hạn: ≤5 nghĩa, ≤3 ví dụ/nghĩa, chuỗi ≤1000 ký tự, `audioUrl`/`source.url` chỉ nhận https; thẻ không có nghĩa nào vẫn hợp lệ (thẻ nháp).
- Học / luyện tập: `FlashcardQuizBuilder` dựng thẻ thành **trắc nghiệm** (đáp án đúng = bản dịch nếu có, không thì định nghĩa; tối đa 3 đáp án nhiễu từ các thẻ khác trong bộ — ưu tiên cùng từ loại, bỏ nghĩa gần trùng, chọn tất định theo hash nên không cần lưu). Response trả `type = "MultipleChoice"`, `originalType = "LanguageFlashcard"`, kèm `questionData.flashcard`. Xem/sửa bộ giữ `type = "LanguageFlashcard"`. `GET .../Test` nhận thêm `LanguageFlashcard` trong `QuestionTypes`.
- **Chưa hỗ trợ** trong bài kiểm tra của lớp / test template (validator từ chối).
- Extension (`/api/Extension`, giới hạn 60 request/phút/người dùng): `GET|PUT Settings`, `GET QuestionSets` (bộ được sửa), `POST Flashcards` (thêm thẻ từ từ được bôi đen), `DELETE Flashcards/{id}` (xoá một thẻ: nút Hoàn tác / Xoá của extension; cần quyền sửa bộ chứa thẻ).
- Bảng `ExtensionSettings` (migration `AddExtensionSettings`): bộ đích Fixed/Daily/Weekly/Monthly, bật/tắt dịch + cặp ngôn ngữ, lưu ngữ cảnh, múi giờ. Bộ theo kỳ tự tạo (Private) tên `Flashcards 2026-10-19` / `Flashcards 2026-W43` / `Flashcards 2026-10` theo múi giờ người dùng; cùng từ (không phân biệt hoa thường) trong cùng bộ không tạo thêm thẻ; tối đa 500 câu/bộ.
- Tra nghĩa phía server: Free Dictionary API (timeout 4s, cache 24h) + dịch MyMemory (timeout 5s, cache 24h), host cố định. **Tắt dịch = không tra gì, thẻ tạo với phần nghĩa trống** (đúng spec). Lỗi dịch vụ ngoài không chặn việc lưu thẻ: `lookup.status` = `found` | `not_found` | `error` | `skipped`.
- `POST /api/Extension/Translate` (tab Dịch của extension): một từ (không có khoảng trắng, ≤40 ký tự) → Free Dictionary + dịch từ (MyMemory); câu / cụm nhiều từ (≤1000 ký tự) → **LibreTranslate tự host** (`LibreTranslate:BaseUrl`, tuỳ chọn `LibreTranslate:ApiKey`; chưa cấu hình / lỗi → `translationStatus = unavailable`, không ném lỗi). Trả kèm `back` dựng sẵn để thêm flashcard không phải tra lại. Docker compose (`deploy/docker-compose.yml`) có thêm service `libretranslate` (chỉ nạp en,vi qua `LT_LOAD_ONLY`, không mở port).
- `GET /api/Extension/Flashcards/Recent?limit=` (mặc định 10, tối đa 50): thẻ flashcard do chính người dùng thêm gần đây (mới nhất trước), chưa bị xoá, trong bộ chưa bị xoá và còn quyền sửa. Nguồn dữ liệu tab "Vừa thêm" của extension.
- Thông báo rõ khi bộ đích cố định của extension đã bị xoá / mất quyền sửa.
- Free Dictionary API hay trả 5xx thoáng qua (HTTP 530 "error code: 1033" của Cloudflare, kể cả với từ có thật): tra từ điển thử lại tối đa 3 lần ngắn khi gặp 5xx; 404 / timeout không thử lại; lỗi tạm thời không được cache.
- Giới hạn đã biết: tạo thẻ đồng thời cùng một từ trong cùng bộ có thể lọt qua kiểm tra trùng (kiểm tra rồi mới ghi, không có ràng buộc DB); Free Dictionary API treo với một số từ (đã gặp với "documentation", "candid"); chưa có hạn mức theo gói cho thẻ.
- Thẻ nháp (chưa có nghĩa) bị loại khỏi Learn / luyện tập; bản dịch trùng đúng từ gốc (MyMemory trả lại khi không dịch được) mà từ điển cũng không có thì không lưu.
- Unit test: `LanguageFlashcardTests`, `CaptureFlashcardCommandTests` (tổng 135).

## [Unreleased] — 2026-10-02

### Added — Chế độ nội dung Markdown cho câu hỏi
- `TextFormat` có giá trị **`Markdown`** (đổi tên từ `MarkDown` chưa từng được dùng; thứ tự / giá trị số giữ nguyên: Markdown=0, PlainText=1, Html=2). EF vẫn lưu dạng chuỗi nên **không cần migration**; converter đọc không phân biệt hoa thường (`Enum.Parse(..., true)`) nên dòng cũ "MarkDown" nếu có vẫn đọc được.
- `CreateUpdateQuestionDto.TextFormat` (tuỳ chọn: "PlainText" | "Html" | "Markdown", không phân biệt hoa thường; validator từ chối giá trị khác). Thêm `TextFormatHelper` (`IsValid`, `Normalize`, `Resolve`).
- Dùng ở mọi nơi tạo / sửa `Question`: `CreateQuestionSetCommand`, `UpdateQuestionSetCommand` (cả nhánh sửa lẫn nhánh thêm), `CreateTestTemplateCommand`, `UpdateTestTemplateCommand`, `CreateTestCommand`, `UpdateTestCommand`. Không gửi `TextFormat` thì giữ mặc định cũ của từng lệnh (Html / PlainText) nên client cũ không đổi hành vi.
- `Deserializer.Deserialize(question)` (response dạng `CreateUpdateQuestionDto`, dùng cho sửa / copy câu hỏi) trả về `TextFormat`. `QuestionResponseDto`, `QuestionAttemptDetailDto`, `ReviewQuestionDto` đã có sẵn `TextFormat`.
- `CompareQuestion` (copy câu hỏi vào test / template, kiểm tra câu nào thực sự đổi): khác `TextFormat` được coi là câu hỏi khác; client cũ không gửi `TextFormat` thì bỏ qua so sánh này.
- Import xlsx: cột **tuỳ chọn** `Text Format` (PlainText | Html | Markdown) trong `FileService`; không có cột / ô trống = như cũ, giá trị lạ làm dòng đó vào danh sách không hợp lệ. File mẫu cũ vẫn dùng được.
- Unit test: `TextFormatTests` (validator, `Resolve`/`Normalize`, enum + converter, `Deserializer`, `Compare`), `ImportTextFormatColumnTests` (xlsx tạo tại chỗ có / không có cột).
- Lưu ý triển khai: backend cần được triển khai trước hoặc cùng frontend; backend cũ bỏ qua `textFormat` và lưu câu Markdown thành Html.


### Fixed — QA vòng 2 (đợt sửa lỗi tài khoản / xác thực)
- **D1 — Khoá tài khoản không nhập lý do**: `BanAccountCommand` lưu `NULL` thay vì chuỗi tiếng Việt mặc định; giao diện tự hiển thị "không có lý do" theo ngôn ngữ.
- **D1 — Lý do auto-ban có mã ổn định**: `ModerationService` lưu `AUTO_BAN_STRIKES:{số vi phạm}:{số ngày}` để frontend dịch vi/en (dữ liệu cũ dạng văn bản tự do vẫn hiển thị bình thường). Email gửi cho user vẫn là văn bản song ngữ VI / EN.
- **N2 — Cửa sổ 401 sau khi mở khoá**: `IUserStatusService.Invalidate(userId)` + `UserStatusService.CacheKey`; `IdentityService` xoá cache trạng thái khi `BanUser` (khoá VÀ mở khoá) và khi đăng nhập thành công (email/mật khẩu, Google, đăng ký) — `IMemoryCache` được thêm vào constructor dưới dạng tham số tuỳ chọn nên test cũ không bị ảnh hưởng. Thêm unit test `Invalidate_ReloadsStatus_AfterBanAndUnban`.
- **N10 — Đổi mật khẩu trùng mật khẩu hiện tại**: `ChangePasswordAsync` trả lỗi mới `ACCOUNT_NEW_PASSWORD_SAME_AS_CURRENT` (mật khẩu hiện tại sai vẫn trả `ACCOUNT_WRONG_PASSWORD`).


### Added — Media cho câu hỏi (ảnh/video) + kiểm duyệt
- **Upload media** `POST /api/Media/Upload` (multipart `file`) và `GET /api/Media/Permissions`.
  - Ảnh được service `media-ai` chuyển sang **JPEG**, thu nhỏ vừa **1920×1080** nếu lớn hơn (không phóng to), xoá metadata.
  - Video được transcode sang **MP4 H.264 ≤720p, `+faststart`** kèm ảnh poster.
  - File lưu trên **MinIO** (bucket private). Kích thước tối đa: ảnh 15MB, video 200MB / 10 phút (cấu hình `Media__*`).
- **Presigned URL ngắn hạn (AWS SigV4)**: `S3PresignedUrlSigner` ký URL cục bộ.
  - Thời hạn: ảnh 15 phút, video 120 phút, để Range request không đứt khi đang xem.
  - URL chỉ xuất hiện trong response của các API đã kiểm tra quyền xem câu hỏi, nên quyền xem media đi theo quyền xem câu hỏi.
  - Thời điểm ký được làm tròn theo cửa sổ 5 phút để URL ổn định, giúp trình duyệt cache được.
- **Gắn media vào câu hỏi**: `CreateUpdateQuestionDto.mediaId`; response có `media { id, type, url, thumbnailUrl, expiresAt }`.
  - Áp dụng cho: question set, test template, test (create/update), attempt và review.
  - `ResolveQuestionMediaAsync` chỉ cho gắn media mình sở hữu, hoặc media nằm trong câu hỏi mình có quyền xem (trường hợp copy câu hỏi). Điều này chặn việc dùng `mediaId` của người khác để tự sinh URL mới.
- **Phân quyền theo gói**: `Plan.CanUploadImage`, `Plan.CanUploadVideo`.
  - Có trong API tạo/sửa/xem gói (Admin).
  - Seed mặc định: *AIQ Plus* được upload ảnh; *AIQ Pro* được upload ảnh và video.
  - Không bị ảnh hưởng bởi `PlanSettings:FreeAccess`.
- **Kiểm duyệt ảnh sau upload** (`ModerationService`, Hangfire):
  - User đăng câu hỏi bình thường. Ngay sau upload sẽ có job kiểm duyệt bằng NudeNet và `jaranohaal/vit-base-violence-detection`.
  - Job `media-moderation-sweep` (5 phút/lần) quét lại các ảnh còn Pending, có retry (tối đa 5 lần, sau đó đánh dấu Failed).
  - Khi ảnh vi phạm:
    - Xoá file trên MinIO.
    - Soft-delete **mọi câu hỏi dùng ảnh** (kể cả bản copy trong test/test template), gỡ khỏi test version và cập nhật lại `QuestionCount`.
    - Ghi một **vi phạm (strike)** cho người upload và gửi email cảnh báo.
- **Strike & auto-ban** (tương tự YouTube):
  - Mỗi vi phạm hết hiệu lực sau **90 ngày**.
  - Job `violation-daily-review` (hằng ngày 02:00 UTC) gỡ dần các vi phạm hết hạn.
  - Có **≥3 vi phạm còn hiệu lực** thì tài khoản **tự động bị ban**, lưu lý do và gửi email.
- **Lý do ban hiển thị khi đăng nhập**: `UserAccount.BanReason` và `User.BanReason/BannedAt`.
  - Login và Google login trả `errors.ACCOUNT_BANNED = ["<lý do>"]`.
  - Admin ban thủ công (`PATCH Users/{id}/Ban`) giờ lưu `message` làm lý do.
- Entity mới `Media` và `UserViolation`, kèm migration `MediaModerationAndFixes`.
- Hook chỉ dùng khi test: `Moderation__TestViolationFilePrefix`. File có tên bắt đầu bằng tiền tố này bị coi là vi phạm, để test luồng strike/ban mà không cần nội dung xấu thật. Mặc định tắt.

### Changed
- **Auth không dùng HttpOnly cookie nữa**:
  - Login, Google login và refresh trả token trong body.
  - Client gửi `Authorization: Bearer <accessToken>`.
  - `RefreshToken` nhận `{accessToken, refreshToken}`, `RevokeToken` nhận `{refreshToken}` trong body.
  - JWT chỉ đọc từ header.
- `RUN_MIGRATIONS=true` cho phép tự migrate và seed khi chạy Production.
- Thêm `PlanSettings:FreeAccess`: mở khoá learn, tạo test và copy câu hỏi khi chưa có cổng thanh toán.
- Thiếu credential AI (Gemini/Google) thì API AI trả 400 `GENERATE_CONTENT_FAILED` thay vì crash khi resolve service.
- Seed bản ghi `SystemSetting` mặc định (trang admin system-settings trước đây báo `SYSTEM_SETTING_NOT_FOUND`).
- Cột điểm `Attempt`, `AttemptQuestion`, `TestGrade`: `numeric(5,2)` → `numeric(9,2)`. Bài có tổng điểm ≥1000 trước đây lỗi khi nộp.
- Validator điểm câu hỏi: tối đa `999`, khớp với cột `numeric(5,2)`. Trước đây cho phép 1000 nhưng lưu lỗi.
- NuGet audit warning (NU1901–NU1904) không còn chặn build. Còn tồn tại: AutoMapper 13.0.1 có advisory, cần nâng cấp.

### Fixed
- **Chấm điểm**:
  - Trắc nghiệm (Partial): ID trùng làm nhân điểm (`[A,A,A]`), điểm có thể vượt điểm tối đa. Nay khử trùng, bỏ ID lạ và giới hạn trong `[0, score]`.
  - Ghép cặp: gửi cặp trùng hoặc gửi mọi tổ hợp vẫn được điểm tối đa (kể cả AllOrNothing). Nay mỗi item trái chỉ tính một cặp.
  - Trả lời ngắn: không còn phân biệt hoa/thường và khoảng trắng thừa.
  - Dữ liệu trả lời `null`, `questionId` trùng hoặc `type` không khớp không còn gây 500. Server lưu theo loại câu hỏi thật.
- **Review bài làm**: hiển thị điểm tổng (`TestGrade`) thay vì điểm của chính attempt đó.
- **HighestScore**: điểm tổng không bao giờ được cập nhật, vì query trả về chính attempt đang tracked.
- **Nộp bài quá giờ**: trước đây chỉ dựa vào job auto-submit.
  - Nay chặn nộp sau `TimeStart + TimeLimit + 30s`.
  - Attempt "đang làm" mà đã quá giờ được chốt lại khi Start.
  - Đồng hồ `TimeRemaining` không vượt quá giờ đóng test.
- **Đáp án lộ khi còn lượt làm**: học viên chỉ thấy đáp án đúng trong review khi đã hết lượt hoặc test đã đóng.
- **Bản ghi trùng do autosave song song**:
  - Thêm unique index `AttemptQuestions(AttemptId, QuestionId)` và `TestGrades(UserId, TestId)`.
  - Start và Review chịu được dữ liệu trùng cũ (không còn `ToDictionary` duplicate key → 500).
- **Copy câu hỏi vào test/template**: so sánh ShortText luôn trả "giống" (`!= null ||`), nên sửa đáp án bị bỏ qua và có thể NullReference. Nay cũng so sánh `Type` và `MediaId`.
- **UpdateTest đổi số bản xáo trộn (NumberOfShuffles)**:
  - Tăng số bản: tạo sai số version (`Range(count+1, n+1)`), và version mới còn chứa câu vừa xoá.
  - Giảm số bản: câu mới bị gắn vào chính các version đang bị xoá.
- **Folder**: user chỉ có quyền ViewOnly có thể thêm template vào folder để share lại. Nay yêu cầu Owner/Editable.
- **Share folder**: `userId` không tồn tại gây lỗi FK (500). Nay trả `USER_NOTFOUND`.
- **Chia cho 0** trong lịch sử làm bài và kết quả lớp khi tổng điểm test = 0.
- `BuyPlan` mua được cả gói đã bị admin tắt (`IsActive = false`).
- Xoá dòng `Console.WriteLine(JwtSettings.SecretKey)`, vốn ghi secret ra log.
- **Đổi mật khẩu sai mật khẩu hiện tại** (`POST /api/Authentication/ChangePassword`): trả `COMMON_SERVER_INTERNAL_ERROR` do so sánh sai mã lỗi Identity (đã đổi sang `IDENTITY_PASSWORD_MISMATCH` qua `CustomIdentityErrorDescriber`). Nay trả `ACCOUNT_WRONG_PASSWORD`.
- **User bị khoá/xoá vẫn dùng được access token còn hạn**: thêm `UserStatusMiddleware` (sau `UseAuthentication`) kiểm tra `IsBanned`/`IsDeleted` qua `UserStatusService` (cache `IMemoryCache` 30 giây). Bị khoá trả HTTP 401 `ACCOUNT_BANNED` kèm lý do, bị xoá/không tồn tại trả 401 `COMMON_UNAUTHORIZED`. `BanUser` xoá toàn bộ refresh token của user bị khoá (`RefreshToken` vốn đã chặn user bị khoá). Có thể mất tối đa 30 giây để hiệu lực do cache.
- **System Settings nhận giá trị không hợp lệ**: `CreateSystemSettingCommand` yêu cầu chi phí input/output và max token >= 1 (phí hệ thống cố định >= 0); giữ trần hiện có. Ghi system settings và tạo/sửa/xoá gói (`CreateUpdatePlanCommand`, `DeletePlanCommand`) nay chỉ Administrator (trước đây Moderator cũng ghi được); đọc vẫn cho Moderator.
- **Mật khẩu**: Register/Reset/Change/SetPassword dùng chung rule `MustBeValidPassword` (tối thiểu 8 ký tự, có chữ cái và chữ số) thay cho `MinimumLength(6)`. `IdentityOptions.Password.RequiredLength` giữ nguyên 2 vì seed dev dùng mật khẩu ngắn.
- **Banned không có lý do**: login/Google login trả `ACCOUNT_BANNED` với lý do rỗng thay vì chuỗi tiếng Việt cố định để frontend hiển thị bản dịch mặc định. Lý do auto-ban giữ nguyên.
- **Quên mật khẩu lộ việc email có tồn tại**: `RequestPasswordReset` trả thành công im lặng khi email không tồn tại hoặc đã bị xoá (chỉ gửi mail khi có tài khoản, giữ nguyên rate-limit); `ResetPassword` với email lạ trả `ACCOUNT_INVALID_RESET_CODE` thay vì `ACCOUNT_NOTFOUND`.
- **Nạp tiền QR**: `GetQrCodeQuery` cho phép số tiền đúng bằng 5000 (>= 5000), thống nhất với frontend.
- **Danh sách bộ câu hỏi công khai lộ email chủ sở hữu**: `QuestionSetForListResponseDto.CreateBy` che email (`a***@domain`) khi FullName đang là email (mặc định lúc đăng ký), qua `DisplayNameHelper.MaskIfEmail`.
- `GET /api/Plan/CurrentPlan` trả danh sách gói còn hạn sắp theo ngày hết hạn giảm dần (phần tử đầu là gói hiện tại).

### Sửa lỗi (QA đợt 2)
- **Thứ tự câu hỏi/đáp án không được giữ sau khi lưu**: thêm `Question.Order` (gán theo index mảng gửi lên khi tạo/cập nhật question set và template); mọi truy vấn chi tiết/edit/copy/learn/test template/test version sắp xếp theo `Order`, rồi `Created`, `Id`. Item JSON có thêm `Position` (thứ tự tác giả); `ShuffleOrder` chỉ dùng khi giao đề (learn/practice/attempt). Dữ liệu cũ (không có `Position`) tự rơi về thứ tự mảng.
- **Practice test 500** khi `numberOfQuestion` vượt số câu có sẵn: `GetTestFromQuestionSetQuery` clamp theo số câu còn lại, bỏ câu đã xoá, không bao giờ ném lỗi; `QuestionSet/{id}/Types` trả thêm `count` mỗi loại (chỉ tính câu chưa xoá).
- **Import .xlsx hỏng** trả 400 `INVALID_FILE_FORMAT` thay vì 500 (`FileService`).
- **Thứ tự danh sách thư viện/tìm kiếm công khai** có tiebreaker `Created` giảm dần rồi `Id` để ổn định.
- **Folder**: không cho tạo folder trùng tên (không phân biệt hoa thường) cùng chủ sở hữu (`FOLDER_ALREADY_EXISTS`), trim tên, chặn tên toàn khoảng trắng khi tạo/sửa.
- **Test template**: `PATCH TestTemplate/{id}` giờ lưu `Description`.
- **Validator câu hỏi**: trắc nghiệm không được có đáp án trùng nội dung (không phân biệt hoa thường, đã trim).
- Test đơn vị mới: `tests/Application.UnitTests/Questions/QuestionOrderingTests.cs`.

### Sửa lỗi (QA đợt 3: lớp/bài kiểm tra)
- **Lịch kiểm tra lệch ngày (UTC+7)**: `GetTestScheduleQuery` trả thêm `TimeStart`/`TimeFinish` thật (`DateTimeOffset`) cho từng bài; khoảng lọc tháng mở rộng ±1 ngày (UTC) để không sót bài sát biên tháng; frontend tự gom/lọc theo ngày địa phương. Nhóm `date` của server vẫn giữ (theo UTC) nên API tương thích ngược.
- **Lịch rỗng với chủ lớp/giáo viên**: schedule giờ gồm cả lớp mà user là `Owner`/`Teacher` (trước chỉ `Student`); loại bỏ test/lớp đã xoá.
- **`UpdateTestCommand` đếm sai `QuestionCount`** (trừ `DeleteQuestionIds` hai lần): đếm theo số câu thực tế của version gốc sau cập nhật.
- **Thứ tự câu hỏi của test không được giữ**: `CreateTestCommand` không còn xáo version gốc (No = 0, trang sửa test đọc version này); `UpdateTestCommand` đặt lại `Order` của version gốc theo đúng thứ tự client gửi (câu được sửa không bị đẩy xuống cuối).
- **`GetReviewTestQuery`** từ chối (`TEST_NOT_FOUND`) khi test hoặc lớp đã bị xoá.
- **`SearchTestInClassQuery`** trả thêm `TimeFinish`, `MaxAttempt`, `UserAttemptCount`, `HasInProgressAttempt` của user hiện tại để frontend ẩn nút làm bài khi hết lượt.
- Test đơn vị: `GetTestScheduleQueryTests` (vai trò Student/Teacher/Owner, người ngoài lớp, lớp/test đã xoá, `TimeStart`/`TimeFinish` thật, biên tháng, tháng 12).

### Sửa lỗi (QA vòng 2, đợt 2)
- **M3 – Review lộ đúng/sai khi đáp án bị ẩn**: `GET /Test/{attemptId}/Review` khi `isShowCorrectAnswer = false` trả `Questions[].Score = null` (`ReviewQuestionDto.Score` đổi sang `float?`); điểm tổng và `Status` giữ nguyên (là điểm học viên được quyền biết). Test đơn vị: `ReviewQuestionDtoTests`.
- **L3 – Đổi tên thư mục trùng tên**: `UpdateFolderCommand` kiểm tra trùng tên không phân biệt hoa thường với các thư mục khác của cùng chủ sở hữu (loại trừ chính nó) → `FOLDER_ALREADY_EXISTS`.
- **L2 – Owner/Teacher không được làm bài**: `StartAttemptTestCommand` từ chối (`NOT_FOUND_STUDENT_IN_CLASS`) khi người dùng là Owner/Teacher của lớp (dùng `ITestService.GetRoleUserInTest`); frontend đã ẩn nút Attempt cho các vai trò này.


### Database
- Migration mới `AddQuestionOrder`: thêm cột `Questions.Order` (int, mặc định 0) và backfill xác định bằng `ROW_NUMBER() OVER (PARTITION BY QuestionSetId ORDER BY Created, Id)` (câu của test template: theo `TestTemplateQuestions`). **Cần chạy migration khi deploy.** Thứ tự đáp án của dữ liệu cũ không khôi phục được (đã bị xáo trộn lúc lưu); chỉ dữ liệu lưu sau bản này mới giữ đúng thứ tự.

### Tests
- Thêm `UserStatusServiceTests`, `PasswordRuleTests`, `DisplayNameHelperTests`; cập nhật functional test quên/đặt lại mật khẩu cho email không tồn tại. `Application.UnitTests`: 48/48 pass.
- Thêm unit test `CheckUserAnswerTests` (chấm điểm, chống gian lận) và `S3PresignedUrlSignerTests`. Toàn bộ `Application.UnitTests`: 35/35 pass.

### Deploy
- Bộ file deploy ở `../deploy`: `docker-compose.yml` (postgres, redis, minio, media-ai, backend, nginx), `nginx.conf` và `deploy.sh`.
- nginx proxy `/aiquizz-media/*` sang MinIO, giữ nguyên Host để chữ ký presigned hợp lệ. Chỉ cho GET/HEAD và hỗ trợ Range.
