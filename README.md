# GitDepsPacker utility

This utility allows to create ```*.gitdeps.xml``` files for Unreal Engine 4.7+.

## How to use

Building:

 * Copy this repository (or link as git submodule) to ```Engine/Source/Programs/GitDepsPacker/```.
 * Build Unreal Engine.

## Generating ```*.gitdeps.xml``` files

You can generate ```*.gitdeps.xml``` files by running command like:

```
Engine\Binaries\DotNET\GitDepsPacker.exe Engine/Content/SomeTool Engine/Source/ThirdParty/SomeTool !**/*.pdb --base-url=http://cdn.local/gitdeps/sometool/ --ignore-proxy --remote-path=1.0 --target=Engine/Build/SomeTool.gitdeps.xml --storage=\\cdn\share\sometool\1.0 --ignore-git
```

This command:

 * Create UEPACK-files and place them to network storage: ```\\cdn\share\sometool\1.0```;
 * Generage Engine/Build/SomeTool.gitdeps.xml file;
 * Remove all packed files from old ```Engine/Build/*.gitdeps.xml``` files.

Pack files will contains:

 * Files in directory ```Engine/Source/ThirdParty/SomeTool```;
 * Files in directory ```Engine/Content/SomeTool```;
 * Excluding ```*.pdb``` files in all directories;
 * Unchanged files already contains in other ```Engine/Build/*.gitdeps.xml``` files will be excluded;
 * Files stored in git will be excluded (--ignore-git flag).
