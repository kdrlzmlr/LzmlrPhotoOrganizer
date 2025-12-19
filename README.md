# LuzumlarPhotos

# Photo & Video Organizer

A powerful, modern WPF desktop application for Windows that automatically organizes your photos and videos into a clean **Year → Month** folder structure based on the actual date taken — extracted from metadata (EXIF for photos, QuickTime for videos).

It intelligently detects and removes **true duplicates** using SHA256 hashing (not just filename or size), saving you gigabytes of storage space.

Built with .NET 8, published as a **single executable** — no installation required!

![App Screenshot](https://ibb.co/bRJhCDFc)  


## Features

- **Accurate Date Extraction**
  - Photos (JPEG, HEIC, TIFF, etc.): Uses EXIF `DateTimeOriginal`
  - Videos (MP4, MOV, M4V): Uses QuickTime creation time
  - Fallback: File creation time if metadata unavailable

- **Smart Duplicate Detection**
  - Uses **SHA256 hash + file size** for 100% accurate deduplication
  - Duplicates moved to a dedicated `Duplicates` folder
  - Detailed report: list of duplicates + total space saved (in GB)

- **Clean Folder Structure**

OrganizedMedia/
├── 2023/
│   ├── 01/
│   ├── 02/
│   └── 12/
├── 2024/
│   ├── 06/
│   └── 11/
├── 2025/
│   └── 12/
└── Duplicates/


- **User-Friendly Interface**
- Simple WPF GUI with folder browsers
- Real-time progress bar (hashing + moving phases)
- Current file status display
- Comprehensive error logging
- Final summary with results and optional detailed duplicate/log report

- **Performance Optimized**
- Parallel hashing with large buffers
- Responsive UI during long operations
- Handles thousands of files efficiently

- **Portable**
- Single `.exe` file (self-contained)
- No installer, no dependencies
- Run from USB, shared drive, or anywhere

## Supported Formats

- **Photos**: `.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tiff`, `.heic`, `.heif`
- **Videos**: `.mp4`, `.mov`, `.m4v`, `.avi`, `.wmv`, `.mkv`

## How to Use

1. Download the latest `PhotoVideoOrganizer.exe` from the [Releases](https://github.com/yourusername/PhotoVideoOrganizer/releases) page.
2. Run the executable (double-click).
3. Click **Browse** to select:
 - **Source Folder**: Where your unorganized media is located (including subfolders)
 - **Target Folder**: Where you want the organized files (can be empty or new)
4. Click **Start Organizing**
5. Wait for completion — progress is shown in real-time
6. Review results:
 - Files organized by Year → Month
 - Duplicates safely moved
 - Optional detailed report of duplicates and any errors

## Safety & Reliability

- Files are **moved**, not copied (source remains untouched until moved)
- Duplicates are preserved in a separate folder
- All errors are logged and reported
- Unique filenames preserved with `(1)`, `(2)` suffixes if needed

## Technology Stack

- **Language**: C# (.NET 8)
- **UI**: WPF
- **Metadata**: [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet)
- **Hashing**: SHA256 (built-in)
- **Published As**: Single-file, self-contained executable

## License

MIT License — feel free to use, modify, and distribute.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

---

Made with ❤️ for anyone tired of messy photo folders.

**Reclaim your storage. Rediscover your memories.**