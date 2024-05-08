# Multiserver 3 - Nautilus Fork

This repository is a fork of AgentDarks447's celebrated project, Multiserver3. It specifically focuses on adjustments to the Nautilus plugin and the exploration of experimental features, which may not be entirely stable and could potentially affect the web tool component of Multiserver. For those looking to employ Multiserver as a game server, the official version is recommended and is available [here](https://github.com/GitHubProUser67/MultiServer3).

### Caution: Use of Nautilus is entirely at your own risk! It is strongly recommended to backup your data beforehand!

<div align="center">
   ## Tab 1: BAR/SDAT/SHARC TOOL
</div>

<div align="center">

![BAR/SDAT/SHARC Tool](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0a378bb7-382a-4ff6-b328-a7fff8cb836c)

</div>

<div align="center">
   ## Tool 1: Home Archive Creator
</div>

<div align="center">

![Home Archive Creator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

</div>

### Important Note:
For inputs into the Archive Creator, it is recommended to utilize the drag-and-drop functionality from Windows File Explorer, as it can manage both folders and zip files. The "click to browse" function currently only supports zip files.

#### Usage Instructions for Archive Creator:
- Drag and drop one or more folders into the application and select the desired archive type before initiating the creation process. This tool is capable of handling extensive operations, such as creating 70,000+ objects in a single operation.
- Insert an 8-byte timestamp to align with the timestamp field in your SDC, if applicable.
- The default output path for the Archive Creator tool is located adjacent to the exe/Output/Archives/. This setting can be altered in the preferences, although it will revert to the default upon restarting the application.

#### Options for Archive Creator:

##### Timestamp:
   - Enter a timestamp here. The default is FFFFFFFF. If less than 8 bytes are entered, they will be padded to 8 bytes with a prefix of 0.
   - If a timestamp.txt file is present in your input folder, this GUI field will be disregarded.

##### Types of Archives:
- **BAR:** The most basic form of Home Archive, historically used in early retail home editions and later restricted to developer versions. These archives are the quickest to read, mount, create, and dump due to their simple zlib compression and lack of additional security layers.
- **BAR Secure:** An encrypted version of BAR used in conjunction with SHA1 in the TSS in some of the earlier pre-sharc versions of Home.
- **SDAT:** Similar to BAR but augmented with an NPD encryption layer.
- **SDAT SHARC:** A secure archive format introduced in version 1.82 to combat hacking and piracy.
- **CORE SHARC:** First introduced in version 1.82+, this format secures local COREDATA sharc files within the client package.
- **Config SHARC:** Employed for encrypting online mode configuration files that are transmitted to clients upon initial connection.

<div align="center">
   ## Tool 2: Home Archive Unpacker
</div>

<div align="center">

![Home Archive Unpacker](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)

</div>

### Important Note:
For the Archive Unpacker, utilizing the drag-and-drop functionality from Windows File Explorer is recommended as it can effectively manage folders. Upon dragging a folder into the Unpacker, it will recursively scan for any compatible archives and automatically add them.

#### Usage Instructions for Archive Unpacker:
- Drag and drop one or more compatible archives or folders into the tool. It is designed to manage large-scale tasks, such as unpacking 70,000+ objects in a single operation.
- The tool generates a timestamp.txt in the output folder containing the original timestamp of the archive, which should be retained for future repacking of that folder.
- The default output path for the Archive Unpacker tool is next to the exe/Output/Mapped/. This setting can be modified in the preferences but will revert to default on the next restart.
- When unpacking objects, if the input archive includes the string "object," then the output folder name will replace this with the UUID (e.g., 00000000-00000000-00000000-0000000B/object_T037.sdat will be extracted to the 00000000-00000000-00000000-0000000B_T037 folder).

#### Options for Archive Unpacker:

##### UUID/Path Prefix:
- A UUID or a complete path prefix can be entered here, which will be appended to any paths identified during the mapping process.
- Additionally, it will scan for any UUIDs present anywhere in the input file path and attempt to utilize them for mapping.

##### Validate Files:
- This feature is enabled by default. Disabling this option can speed up the mapping process for bulk tasks, although it is not recommended.
- If enabled, this feature will attempt to ensure that all files have been correctly dumped. It employs a combination of header/string byte-level checks and specific libraries to check media files such as mp3, wav, png, jpg.
- It also analyzes xml, json, scene, sdc, odc, etc., searching for indications of corruption, encryption, or compression.
- HomeLuac.exe is used to verify Lua files for syntax errors, which may occasionally result in false flags, as developers sometimes wrote non-standard Lua. However, this check is generally beneficial.
- The tool will also record any 0-byte files for additional inspection. Although this sometimes results in false flags, as developers occasionally used 0-byte files, it is advantageous to flag these for further review.
- Lastly, any items with unmapped files will be logged. This check occurs regardless of whether the validate files feature is enabled. If unmapped files are detected, a _CHECK suffix will be added to the output folder name.
- Should any warnings or file validation failures occur during the validation process, a JobReport.txt will be generated in your output folder.

##### Offline Structure:
- This setting affects only the extraction of objects; when enabled, it extracts objects into the "offline" folder structure with all files and folders at the root level. This configuration is ideal for running in extracted form on HDK builds.
- This was the standard practice until recently. If you have previously extracted objects using older tools like Gaz's HomeTool, you will be familiar with this structure. However, it is not the correct folder structure needed for rebuilding archives, so caution is advised.
- Given the revival of Online, this is no longer the default folder structure required, thus it is not set as the default. It is important to note that unlike other settings, this one does not persist between sessions.

<div align="center">
   ## Tab 2: SceneID Generator / Decrypter
</div>

<div align="center">

![SceneID Generator / Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4204a35c-9dea-40d5-afed-5367d5e8fb75)

</div>

<div align="center">
   ## Tool 1: Scene ID Generator
</div>

<div align="center">

![Scene ID Generator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fbc2f728-4c22-4f3d-9c2f-00091be53052)

</div>

### Note:
Scene IDs, also known as Channel IDs, are critical for instancing in PlayStation Home. If two scenes share a Scene ID listed in SceneList.xml, players will be placed into the same instance. Generally, each scene must have a unique Scene ID, although under certain conditions where scenes are sufficiently similar, sharing IDs may be feasible.

#### Usage:
- Enter a number between 1 and 65535 and click 'Encrypt'.
- The tool also accepts hyphen-separated ranges for bulk generation.

#### Options:
- Typically, there is no need to adjust this setting; leave it on default (Disabled) for newer versions of Home.
- When enabled, Legacy Mode allows for the generation of early type IDs suitable for older Home builds.

<div align="center">
   ## Tool 2: Scene ID Decrypter
</div>

<div align="center">

![Scene ID Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4e5679fa-3fe0-4cf4-b393-651b15a7384c)

</div>

### Note:
As previously mentioned, Scene IDs are essential for correct instancing in PlayStation Home. It is possible to share IDs between similar scenes, but this should be approached with caution.

#### Usage:
- Enter one or more Scene IDs (separated by spaces, commas, or lines).
- You can also drag a plaintext SceneList.xml into the right-hand side of the tab, and it will parse and decrypt all IDs. Ensure not to drag the XML directly onto the textbox.

#### Options:
- Generally, this setting does not require modification; keep it on default (Disabled) for newer Home versions.
- Legacy Mode, when activated, facilitates the decryption of early type IDs suitable for earlier Home builds.
