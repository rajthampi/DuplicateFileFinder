# Duplicate File Finder

A Windows desktop application built with WinUI 3 and .NET 8 for locating and safely removing duplicate files.

## Features

- Search recursively through a selected folder and its subdirectories.
- Filter files using one or more patterns, such as `*.jpg`, `*.png`, or `*.*`.
- Identify candidates by file size before calculating SHA-256 hashes.
- Compare file hashes to detect duplicate content.
- Display source and duplicate file paths, file sizes, thumbnails, and duplicate groups.
- Select individual duplicate files or select all results.
- Delete only the checked duplicate files.
- Optionally move deleted files to the Windows Recycle Bin.
- Report scan and deletion progress in the status bar.
- Load thumbnails asynchronously using Windows Shell thumbnail providers with image decoding fallback.
- Open a file with its associated Windows application by clicking its thumbnail.
- Resize result columns using draggable header handles.
- Use horizontal and vertical scrolling for large result sets.
- Use a dark metallic visual theme.
- Use the configured application icon for the executable and window.

## Requirements

- Windows 10 version 1809 or later.
- .NET 8 SDK.
- Visual Studio 2022 with Windows App SDK and WinUI development support.

## Build

1. Clone the repository:

   git clone https://github.com/rajthampi/DuplicateFileFinder.git
   cd DuplicateFileFinder

2. Open `DuplicateFileFinder.sln` in Visual Studio 2022.
3. Restore NuGet packages.
4. Select an appropriate target such as `x64`.
5. Build and run the project.

The project targets `.NET 8` and supports `x86`, `x64`, and `ARM64` Windows configurations.

## Usage

1. Select **Browse...** to choose a folder, or enter a folder path manually.
2. Enter file patterns separated by commas, semicolons, or spaces.
3. Select **Search**.
4. Review the duplicate paths and file sizes in the results list.
5. Check the duplicate files to remove. The source file is protected and is not deleted.
6. Enable the Recycle Bin option when files should be recoverable.
7. Select **Delete**.

Clicking a thumbnail opens the selected file with its associated Windows application.

## How Duplicate Detection Works

1. Matching files are enumerated recursively.
2. Files are grouped by size to avoid hashing files that cannot be duplicates.
3. Candidates are hashed with SHA-256.
4. Files with matching hashes are presented as duplicate groups.
5. The first file in each group is treated as the source; only selected duplicate entries are eligible for deletion.

## Safety

- Files are never deleted automatically after a scan.
- Only checked duplicate entries are processed.
- The source file for each duplicate group is protected.
- Recycle Bin deletion is enabled by default.
- File-system and authorization errors are reported in the interface.

## Project Structure

- `MainWindow.xaml` — main window layout, result list, status bar, theme usage, and interactions.
- `MainWindow.xaml.cs` — scanning, hashing, thumbnail loading, deletion, and selection logic.
- `App.xaml` — application resources and metallic theme styles.
- `DuplicateFileFinder.csproj` — .NET target framework, Windows configurations, package references, and application assets.

## License

No license has been specified yet.

### Changes Made:
- Added a title for the project at the beginning.
- Ensured consistent formatting and clarity throughout the document.
- Enhanced the "How Duplicate Detection Works" section title for better readability.
- Maintained the original structure while ensuring the flow of information is logical and coherent.