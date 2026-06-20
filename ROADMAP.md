# ClearTool — Roadmap

> 📌 Danh sách tính năng đề xuất cho các phiên bản tới. Đây là tài liệu **định hướng**, không phải cam kết — thứ tự có thể đổi theo nhu cầu thực tế.
> Lịch sử thiết kế & quyết định kỹ thuật của bản hiện tại: xem [PLAN.md](PLAN.md). Tài liệu người dùng: [README.md](README.md).
>
> Cập nhật: 2026-06-20 · Bản hiện tại: **v1.1.1**

---

## Trạng thái hiện tại

**Đã xong (Phase 0–5 trong PLAN.md):** quét toàn ổ (error-tolerant, skip reparse, bỏ qua OneDrive placeholder, long-path), treemap squarified (DrawingVisual, virtualize), engine phân loại an toàn (Safe/Caution/Keep) + RuleCatalog, xóa qua `IFileOperation` + hàng rào `ProtectedRoots` độc lập, Empty Recycle Bin, WinSxS (DISM), nâng quyền theo tác vụ, UI Fluent/Mica theo theme.

**Đã làm thêm ngoài plan gốc:**
- 🔄 Tự kiểm tra & cập nhật từ GitHub Releases (banner "Tải & cập nhật" 1 chạm).
- ⚙️ CI/CD bằng GitHub Actions: CI build+test mọi push/PR; Release tự build exe + tạo release khi push tag `v*`.
- ⚡ Virtualize danh sách gợi ý + overlay loading (hết đơ sau khi quét).

**Còn sót trong PLAN.md (đều là loại tùy chọn / đã xử lý gián tiếp):**
- Quét **song song** (`ScanOptions.DegreeOfParallelism > 1`, fan-out `Parallel.ForEach`) — option đã có nhưng bị bỏ qua, scanner vẫn đơn luồng. Plan ghi rõ là "MVP đơn luồng, sau có thể fan-out". Lợi ích không chắc (quét đĩa nghẽn I/O hơn CPU).
- Cảnh báo **long-path (>260)** trước khi xóa — không có cảnh báo UI riêng, nhưng đã được giải quyết gián tiếp khi chuyển sang `IFileOperation` (hỗ trợ long-path tốt hơn VB FileIO).

---

## Quy ước

| Ký hiệu | Ý nghĩa |
|---|---|
| ⭐ | Nên làm trước (lợi nhiều / tốn ít hoặc đúng nỗi đau gốc) |
| 💎 Giá trị | Mức hữu ích cho người dùng |
| 🔧 Công sức | Ước lượng khối lượng triển khai |

Nỗi đau gốc của tool: **máy dev, ổ C hay đầy vì cache (npm/pip, VS Code, Docker, WSL…)** — ưu tiên bám theo đây.

---

## A. Phân tích sâu hơn (bổ trợ treemap)

| Tính năng | 💎 Giá trị | 🔧 Công sức | Ghi chú |
|---|---|---|---|
| ⭐ **Top N file/thư mục lớn nhất** | Cao | Thấp | Danh sách phẳng "100 mục ngốn nhất ổ" — bù điểm yếu treemap khó soi file lẻ. Đã có sẵn cây, chỉ cần sắp xếp + view mới. |
| ⭐ **Lọc theo đuôi / ngày / kích thước** | Cao | Thấp | Vd "tất cả `.iso`/`.vhdx` > 1 GB", "file không đụng > 1 năm". Lọc trên cây đã quét. |
| **Tìm file trùng lặp (duplicate finder)** | Cao | Trung bình | Hash theo nhóm cùng size (+ partial hash) để gợi ý xóa bản thừa: ảnh, installer, dataset. |
| **So sánh 2 lần quét (snapshot diff)** | Cao | Trung bình | Lưu ảnh chụp, lần sau chỉ ra thư mục nào phình thêm → trả lời "vì sao ổ C đầy thêm 10 GB tuần này". |

## B. Dọn dẹp thông minh hơn

| Tính năng | 💎 Giá trị | 🔧 Công sức | Ghi chú |
|---|---|---|---|
| ⭐ **Mở rộng RuleCatalog** | Rất cao | Thấp | Thêm nguồn rác phổ biến: **Docker images/volumes, WSL `.vhdx`, `Windows.old`, Delivery Optimization, Prefetch, crash dumps/WER, thumbnail cache, Teams/Discord/Spotify, Steam/Epic shader cache, Adobe cache**… Đây là "nội dung" cốt lõi — càng giàu rule càng hữu ích, gần như không rủi ro. |
| **Danh sách loại trừ (exclusion/whitelist)** | Cao | Thấp | User đánh dấu "đừng bao giờ gợi ý xóa thư mục này". |
| **Quy tắc tùy chỉnh (user rules)** | Trung bình | Trung bình | Cho user tự thêm path/glob của họ vào catalog. |
| **Gỡ phần mềm + dọn tàn dư** | Trung bình | Cao | List app đã cài kèm size, gỡ + dọn. Mở rộng phạm vi tool. |

## C. An toàn & tin cậy

| Tính năng | 💎 Giá trị | 🔧 Công sức | Ghi chú |
|---|---|---|---|
| ⭐ **Lịch sử xóa + hoàn tác** | Cao | Trung bình | Log đã xóa gì / khi nào / bao nhiêu; nút khôi phục cho mục còn trong Thùng rác. Tăng độ tin cậy rõ rệt. |
| **Xuất báo cáo (CSV/JSON/HTML)** | Trung bình | Thấp | Cho dân IT lưu/đối chiếu kết quả quét. |

## D. Trải nghiệm người dùng

| Tính năng | 💎 Giá trị | 🔧 Công sức | Ghi chú |
|---|---|---|---|
| ⭐ **Quét 1 thư mục cụ thể** | Cao | Thấp | Không chỉ cả ổ — vd chỉ `C:\Users\me\Projects`, hoặc kéo-thả folder vào để quét. |
| ⭐ **Tray icon + cảnh báo ổ sắp đầy** | Cao | Trung bình | Chạy nền, nhắc "ổ C còn < 5 GB" → đúng nỗi đau gốc. |
| **Trang Settings** | Trung bình | Thấp | Gom tùy chọn: mặc định Recycle Bin/Permanent, ngưỡng cảnh báo, bật/tắt tự-update, ngôn ngữ. |
| **Đa ngôn ngữ (English)** | Trung bình | Trung bình | UI hiện hardcode tiếng Việt; repo/README tiếng Anh → thêm English mở rộng người dùng. |
| **Ô tìm kiếm** trong treemap/gợi ý | Trung bình | Thấp | Lọc nhanh theo tên. |

## E. Hiệu năng & kỹ thuật

| Tính năng | 💎 Giá trị | 🔧 Công sức | Ghi chú |
|---|---|---|---|
| **Quét bằng NTFS MFT trực tiếp** | Cao | Cao | Đọc Master File Table như WizTree → nhanh gấp hàng chục lần, nhưng cần admin + đọc raw volume. Khác biệt lớn nhất về tốc độ. |
| **Dọn Shadow Copy / System Restore** | Trung bình | Trung bình | Phần "X GB không truy cập được" app đang chỉ báo cáo — có thể cho dọn luôn (vssadmin), kèm cảnh báo mất điểm khôi phục. |
| **Quét song song** | Thấp–TB | Trung bình | Thứ duy nhất còn sót trong plan. Nên đo thực tế trước (I/O-bound, lợi ích không chắc). |

---

## 🎯 Đề xuất "next up" (xếp theo lợi nhiều / tốn ít)

1. **Mở rộng RuleCatalog** (Docker/WSL/Windows.old…) — đào sâu đúng thế mạnh, gần như không rủi ro.
2. **Top N file lớn nhất + bộ lọc** — bù điểm yếu của treemap.
3. **Quét 1 thư mục cụ thể** — nhu cầu hay gặp.
4. **Lịch sử xóa + hoàn tác** — tăng niềm tin người dùng.
5. **Tray + cảnh báo ổ đầy** — khớp lý do tạo ra tool.

> Trước khi code bất kỳ mục nào: phân tích phương án (luồng dữ liệu, rủi ro an toàn, chỗ móc vào code hiện có), đặc biệt các mục đụng tới **xóa** phải đi qua hàng rào `ProtectedRoots`.
