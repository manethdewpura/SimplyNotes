# SimplyNotes

A lightweight, Windows-native desktop Simply notes application built with C# and WPF (.NET 8.0).

## Features

- **Frameless UI:** Clean, modern design with rounded corners and drop shadows.
- **Customizable Colors:** Choose from 5 soft pastel themes (Yellow, Blue, Green, Pink, Purple).
- **Categorization (Topics):** Add a custom topic/category label to the bottom of each note (e.g., "Work", "Ideas", "Shopping"). Double-click to edit!
- **Auto-Saving:** Notes are automatically saved as you type or move them, using a 500ms debounce timer for optimal performance.
- **Minimal Footprint:** No third-party libraries, pure standard WPF controls.
- **Atomic File Operations:** Thread-safe, atomic JSON saving to ensure your data is never corrupted, even in the event of a crash.

## Data Storage

Your notes are saved locally in a single JSON file:
`%APPDATA%\SimplyNotes\notes.json`

## Requirements

- Windows OS
- [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (or SDK if you want to build it yourself)

## Building & Running

1. Clone the repository:

   ```bash
   git clone https://github.com/manethdewpura/SimplyNotes.git
   cd SimplyNotes
   ```

2. Build and run via the .NET CLI:

   ```bash
   dotnet run --project SimplyNotes.csproj
   ```

   _Alternatively, open `SimplyNotes.slnx` in Visual Studio or JetBrains Rider and hit Run._

## Architecture

The project is structured into three main layers:

- **`Models/`**: Contains the immutable-friendly `NoteData` representing the persisted state of a note.
- **`Services/`**: Contains the thread-safe JSON persistence layer (`NoteStore`) and the color theming engine (`NoteTheme`).
- **`Views/`**: Contains the XAML definitions and code-behind for the UI (`MainWindow`).

## Contributing

Feel free to open an issue or submit a pull request if you have ideas for improvements or find any bugs!
