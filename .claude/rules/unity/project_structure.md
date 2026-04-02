# Unity Project Structure

Assets are organized into top-level folders by type, each with feature subfolders:

```
Assets/
├── Prefabs/
│   └── [Feature]/       # e.g. Samples/, UI/, Units/
├── Scenes/
│   └── [Feature]/
└── Scripts/
    └── [Feature]/       # contains .cs files and one .asmdef per feature folder
```

- Every feature folder under `Scripts/` has exactly one `.asmdef` file named after the folder (e.g. `Samples.asmdef` inside `Scripts/Samples/`)
- Prefabs and scenes mirror the same feature subfolder names used in Scripts
- Do not put assets directly under `Assets/` root (except Unity-generated files like `InputSystem_Actions.inputactions`)
