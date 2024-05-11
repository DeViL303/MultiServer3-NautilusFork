<h1 align="center">
   Multiserver 3 - Nautilus Fork
   </h1>

This repository is a fork of AgentDarks447's awesome project, Multiserver3. It specifically focuses on adjustments to the Nautilus plugin and the exploration of experimental features, which may not be entirely stable and could potentially affect the web tool component of Multiserver. For those looking to employ Multiserver as a game server, the official version is recommended and is available [here](https://github.com/GitHubProUser67/MultiServer3).

<h2 align="center">
   Tab 1: BAR/SDAT/SHARC TOOL
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/584930ff-098f-4180-bf74-8e7d834ee0d4)


</div>

### Caution: Use of Nautilus is entirely at your own risk! It is strongly recommended to backup your data beforehand!

<h2 align="center">
   Home Archive Creator
</h2>

<div align="center">
   
![Home Archive Creator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

</div>

### Important Note:
For inputs into the Archive Creator, it is recommended to use the drag-and-drop functionality from Windows File Explorer, as it has support for both folders and zip files. The "click to browse" function currently only supports choosing zip files.

#### Usage Instructions for Archive Creator:
- Drag and drop one or more folders into the application and select the desired archive type before initiating the creation process. This tool is capable of handling extensive operations, such as creating 70,000+ objects in a single operation.
- Insert an 8-byte timestamp to align with the timestamp field in your SDC, if applicable.
- The default output path for the Archive Creator tool is located adjacent to the exe in Output/Archives/. This setting can be altered in the preferences, although it will revert to the default upon restarting the application.

#### Options for Archive Creator:

##### Timestamp:
   - Enter a timestamp here. The default is FFFFFFFF. If less than 8 bytes are entered, they will be padded to 8 bytes with a prefix of 0.
   - If a timestamp.txt file is present in your input folder, this GUI field will be disregarded.

##### Types of Archives:
- **BAR:** The most basic form of Home Archive, historically used in early retail home editions and later restricted to developer versions. These archives are the quickest to read, mount, create, and dump due to their simple zlib compression and lack of additional security layers.
- **BAR Secure:** An encrypted version of BAR used in conjunction with SHA1 in the TSS in some of the earlier pre-sharc versions of Home.
- **SDAT:** Similar to BAR but augmented with an NPD encryption layer.
- **SDAT SHARC:** A secure archive format introduced in version 1.82 to combat hacking and piracy. These are both encrypted with NPD key, and with content server key found in the TSS.
- **CORE SHARC:** First introduced in version 1.82+, this format secures local COREDATA sharc files within the client package. These are encrypted with a local key that is built into 1.82+ Retail EBOOTS.
- **Config SHARC:** Employed for encrypting online mode configuration files that are transmitted to clients upon initial connection. These are encrypted with the content server key but no NPD layer.

<h2 align="center">
   Home Archive Unpacker
</h2>

<div align="center">
   
![Home Archive Unpacker](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)

</div>

### Important Note:
For the Archive Unpacker, utilizing the drag-and-drop functionality from Windows File Explorer is recommended as it has support for adding folders. Upon dragging a folder into the Unpacker drag area, it will recursively scan for any compatible archives and automatically add all of them. 

#### Usage Instructions for Archive Unpacker:
- Drag and drop one or more compatible archives or folders into the tool. It is designed to manage large-scale tasks, such as unpacking 70,000+ objects in a single operation.
- The tool generates a timestamp.txt in the output folder containing the original timestamp of the archive, which should be retained for future repacking of that folder if you dont want to have to edit timestamps field in SDC.
- The default output path for the Archive Unpacker tool is next to the exe in Output/Mapped/. This setting can be modified in the preferences but will revert to default on the next restart.
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
- This was the standard practice until recently. If you have previously extracted objects using older tools like Gaz's HomeTool, you will be familiar with this structure. However, it is not the correct folder structure needed for rebuilding archives, so caution is advised. Only enable this option if you dont intend to repack the objects into archives.
- Given the revival of Online, this is no longer the default folder structure required, It is important to note that unlike other settings, this one does not persist between sessions.


<h2 align="center">
   Tab 2: CDS Tool - SDC/ODC/SceneList Decrypter / Encrypter
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/c22b0dfa-4e93-43c5-a324-072b451ecf78)

</div>

This tab handles all the smaller xml files that are encrypted with their SHA1. 99.9% of the time you dont need to supply any SHA1 as it can use another method to get the IV, however in some rare cases where the file has a non standard header you might need to supply a SHA1 to decrypt. 

<h2 align="center">
   CDS Encrypter Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/33e48df1-ee99-4871-b789-bce4a71bc7ec)

</div>

### Note:
- The CDS encrypter tool will automatically generate the SHA1 for input files and then use the first 16 bytes of that SHA1 to encrypt the file.
- Optionally you can choose to append the original SHA1 to the output filenames. This essentially means the decryption key is attached to the file.
- CDS Encrypter tool by default will output to Output/CDS/ next to the exe.

<h2 align="center">
   CDS Decrypter Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/50c46f3d-246c-4c44-8a98-ae98295aa1a5)

</div>

### Note:
- CDS Decrypter Tool can decrypt almost all ODC / SDC / SceneList xmls wihout any SHA1 provided. It uses an exploit to "work backwards" and "guess" the IV used.
- This exploit will work as long as the header of the xml has one of several expected byte sequences. 
- In rare cases where there is a modified header you will need to supply a sha1 for decryption, this can be supplied either in the filename, or typed into SHA1 input box.
- If any SHA1 is typed into input box it will override the SHA1 found in filename if one exists.
- CDS Encrypter tool by default will output to Output/CDS/ next to the exe.

<h2 align="center">
   Tab 3: HCDB Encrypter / Decrypter
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0fa29f04-ee86-4a40-92de-0b228e2a681a)

</div>

### Notes:
- Playstation Home HCDB object catalogues are natively SQL files that are LMZA compressed to a segs file which reduces the filesize by about 80%.
- Then that segs file (compressed SQL) is encrypted in a similar way to SDC/ODC with the first 16 bytes of the segs SHA1 used as the encryption IV.
- Currently you DO need to supply the segs files SHA1 to decrypt. In later versions it will be able to brute force the IV as most bytes of the segs file header are known.

<h2 align="center">
   SQL to HCDB Encrypter Tool
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fb01dd1a-6e29-42e5-a0d1-fc0b00421e5a)

</div>

<h2 align="center">
   Tab 4: SceneID Generator / Decrypter
</h2>

<div align="center">
   
![SceneID Generator / Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4204a35c-9dea-40d5-afed-5367d5e8fb75)

</div>

### Note:
Scene IDs, also known as Channel IDs, are critical for instancing in PlayStation Home. If two scenes share a Scene ID listed in SceneList.xml, players will be placed into the same instance. Generally, each scene must have a unique Scene ID, although under certain conditions where scenes are VERY similar, sharing IDs may be feasible.

<h2 align="center">
  Scene ID Generator
</h2>

<div align="center">
   
![Scene ID Generator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fbc2f728-4c22-4f3d-9c2f-00091be53052)

</div>

#### Usage:
- Enter a number between 1 and 65535 and click 'Encrypt'.
- The tool also accepts hyphen-separated ranges for bulk generation.

#### Options:
- Legacy Mode, when activated, facilitates the decryption of early type IDs suitable for earlier Home builds.
- Generally, this setting does not require modification; keep it on default (Disabled) for newer Home versions.

<h2 align="center">
   Scene ID Decrypter
</h2>

<div align="center">
   
![Scene ID Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4e5679fa-3fe0-4cf4-b393-651b15a7384c)

</div>

#### Usage:
- Enter one or more Scene IDs (separated by spaces, commas, or lines).
- You can also drag a plaintext SceneList.xml into the right-hand side of the tab, and it will parse and decrypt all IDs. Ensure not to drag the XML directly onto the textbox.

#### Options:
- Legacy Mode, when activated, facilitates the decryption of early type IDs suitable for earlier Home builds.
- Generally, this setting does not require modification; keep it on default (Disabled) for newer Home versions.


<h2 align="center">
   Tab 5: LUA / LUAC TOOL
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/580dc901-efda-417c-937e-b9926a80203e)

</div>

<h2 align="center">
  Home LUA Compiler
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/1bd9326b-06e2-4395-aada-9adc0cc3494c)

</div>

This Tool uses HomeLuaC.exe to parse and/or compile LUA to LUAC for Playstation Home. Generally not much point in compiling LUA to LUAC for home but parsing it to check for syntax errors can be useful.

### Note: 
If you have the "Validate Files" option enabled in TAB 1 mapper tool it will automatically use HomeLuaC.exe to check all mapped LUA files for errors.

### Usage:
- Drag LUA files or folders containing LUA files into the tools drag area.
- It will scan all sub folders recursively for LUA files and add all to the current task

### Options:
- Parse Only: This will just run all LUA files through the compiler but with the argument -p enabled. Nothing will be compiled. This will log any syntax errors found in the gui text area.
- Strip Debug Info: When compiling LUA to LUAC this option adds the argument -s which will remove extra debug information - stripping this information potentially makes future modification more difficult and adds a small bit of security. 

<h2 align="center">
   Home LUAC Decompiler
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/03ee4b4a-1cc8-442f-aa95-ba883c7c87ca)

</div>

### Warning: 
Decompiling LUAC back to LUA is hit and miss so dont expect too much. In some cases you might be able to use the decompiled output in place of the original LUAC but most often not. 
This tool is meant more to be used as an aid to try gain insight into how some LUAC is working. In some cases it might even be preferable to hex edit the LUAC file directly
for example if changing https to http.

### Usage:
- Drag LUAC files or folders containing LUAC files into the tools drag area.
- It will scan all sub folders recursively for LUAC files and add all to the current task

### Options:
- UnLuac.NET: Default option - This seems to be best decompilation we have. 
- UnLuac JAR v1.22: Added this before I knew about the .NET version. Limited use now, might remove them at some point. 
- UnLuac JAR Dec 2023: Added this before I knew about the .NET version. Limited use now, might remove them at some point.

### Note: 
In rare cases Java based UnLuac might give better results but its unlikely. I'm leaving these options in for now as theyre not doing any harm really. 
You could also switch out the JAR files for others if you find better solutions. See Dependencies folder.

<h2 align="center">
   Tab 6: SDC / ODC Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/8cb5ccb0-da2b-4de3-bf0b-c7d2f056fca6)

</div>

### SDC Files:
- Small XML based files used for storing scene information
### ODC Files:
- Small XML based files used for storing object information

### Usage: 
- Fill out the input fields and click create
- For objects you can generate random UUIDs here too.

### Options:
- SDC Offline Mode: When enabled it will leave out the archive section of the xml which is not needed for offline builds.


<h2 align="center">
   Tab 7: Path2Hash Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/1eb8ed66-001b-41ed-901b-ebd4dfd27011)

</div>

Use this tab for debugging mapping issues. It allows you to attempt to discover the paths to unmapped files.

If you know the path to a file and it refuses to map normally, you can add the path here by clicking "Add to Mapper". Once added if this file is ever encountered again it will map automatically.


<h2 align="center">
   Tab 8: Home EBOOT Patcher
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/ceb3bbc8-5174-492e-9835-8a2b893f9668)

</div>

View and or Patch various fields in Home EBOOTS. 

### Work in Progress

<h2 align="center">
   Tab 9: SHA1 Checker
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/5d73316f-cf00-45e7-8147-ef37ff0c3b74)

</div>

Used for checking SHA1s and building lists for comparison

### Work in Progress

Currently decryption mode is not linked up to the backend.

Verify Content: This is a standalone function that allows you to choose a folder of mapped content to run through the file validator function. 

<h2 align="center">
   Tab 10: Settings
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/f1b23a25-25ff-4a02-b7a8-5331fbd2a4b9)

</div>

Set your output paths and various other options here. 

At first boot, a settings.xml is created next to the exe. This also sets all output paths next to the exe in Outputs folder. 

Debug Logs: If this is enabled you will find detailed logs of all operations performed in logs/debug.log next to the exe. 
