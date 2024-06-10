<h1 align="center">
   Nautilus Toolset v1.00 Beta Build 00026
</h1>

<h2 align="center">
   
Download Latest Compiled Release [HERE](https://github.com/DeViL303/MultiServer3-NuatilusFork/releases/download/WIP00026/Nautilus_Beta_00026.zip)

</h2>

<h1 align="center">
   Multiserver 3 by AgentDark447 - Nautilus Fork by DeViL303
   </h1>

This repository is a fork of AgentDark447's awesome server project, Multiserver3. This repo specifically focuses the development of a GUI addon that I call Nautilus, The changes made during the development of Nautilus could potentially affect the web tool or server component of Multiserver. For those looking to employ Multiserver as a game server, the official version is highly recommended and is available [HERE](https://github.com/GitHubProUser67/MultiServer3).

Credits to the developers of Horizon, Multiserver3, UnluacNET, Unluac jar, scetool, HomeLuac :), create_pkg, vgmstream. More detailed credits to come. 

<h1 align="center">
  Playstation Home Cache Archive
   </h1>
   <div align="center">
      
Rew's Playstation Home cache archive can be found here: https://xethub.com/pebxcvi/PSHomeCacheDepot/

</dev>
<h1 align="center">
   What is it?
   </h1>
   
Nautilus is a Windows GUI that I designed to work on Playstation Home assets on a deeper level than has ever been possible before. The end goal is to have an All-in-One solution for automating common tasks and dealing with the many custom file types used in Home. 

Note: Due to the nature of Homes millions of assets and the many edge cases involved this application will most likely never be "finished".


<div align="center">


</div>


<h1 align="center">
   Tab 1: BAR/SDAT/SHARC TOOL
</h1>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/d6683e1b-5494-4f0f-a88a-403c0c58c885)



</div>

### Important: Use of Nautilus is entirely at your own risk! It is recommended to backup your data beforehand!

<h2 align="center">
   Home Archive Creator
</h2>



<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/228eaad1-84ed-48c4-9099-4242ab975d77)

</div>

### Important Note:
For inputs into the Archive Creator, it is recommended to use the drag-and-drop functionality from Windows File Explorer, as it has support for both folders and zip files. The "click to browse" function currently only supports choosing zip files.

#### Usage Instructions for Archive Creator:
- Drag and drop one or more folders into the application and select the desired archive type before initiating the creation process. This tool is capable of handling extensive operations, such as creating 70,000+ objects in a single operation.
- Insert an 8-byte timestamp to align with the timestamp field in your SDC, if applicable.
- The default output path for the Archive Creator tool is located adjacent to the exe in Output/Archives/. This setting can be altered in the preferences.
#### Options for Archive Creator:

##### Timestamp:
   - Enter a timestamp here. The default is FFFFFFFF. If less than 8 bytes are entered, they will be padded to 8 bytes with a prefix of 0.
   - If a timestamp.txt file is present in your input folder, this GUI field will normally be disregarded.

##### Types of Archives:
- **BAR:** The most basic form of Home Archive, historically used in early retail home editions and later restricted to developer versions. These archives are the quickest to read, mount, create, and dump due to their simple zlib compression and lack of additional security layers.
- **BAR Secure:** An encrypted version of BAR used in conjunction with SHA1 in the TSS in some of the earlier pre-sharc versions of Home.
- **SDAT:** Similar to BAR but augmented with an NPD encryption layer.
- **SDAT SHARC:** A secure archive format introduced in version 1.82 to combat hacking and piracy. These are both encrypted with NPD key, and with content server key found in the TSS.
- **CORE SHARC:** First introduced in version 1.82+, this format secures local COREDATA sharc files within the client package. These are encrypted with a local key that is built into 1.82+ Retail EBOOTS.
- **Config SHARC:** Employed for encrypting online mode configuration files that are transmitted to clients upon initial connection. These are encrypted with the content server key but no NPD layer.

#### Rename for CDN
   - This setting when enabled will rename created object archives to suit CDN naming format if certain conditions are met:
     - The input folder must have the UUID in the name like 00000000-00000000-00000000-0000000B_T035
     - The Archive type must be set to either BAR, SDAT or SDAT SHARC.
     - If those conditions are met it will rename the output to suit online CDN use:
     - eg: Objects/00000000-00000000-00000000-0000000B/object_T037.sdat
     - eg: Objects/00000000-00000000-00000000-0000000B/object_T037.bar
    
   - This setting when enabled will also rename scenes to suit CDN naming format if certain conditions are met:
     - The input folder must have a $ in the name like Sci_Fi_Apt_A08E_30B2$scifi_T037
     - The Archive type must be set to either BAR, SDAT or SDAT SHARC.
     - If those conditions are met it will rename the output to suit online CDN use:
     - eg: Scenes/Sci_Fi_Apt_A08E_30B2/scifi_T037.sdat
     - eg: Scenes/Sci_Fi_Apt_A08E_30B2/scifi_T037.bar

#### Rename For Local
- This setting when enabled will rename objects to suit local USRDIR use if certain conditions are met:
     - The input folder must have the UUID in the name like 00000000-00000000-00000000-0000000B_T035
     - The Archive type must be set to either BAR or CORE SHARC.
     - If those conditions are met it will rename the output to suit OFFLINE and "Semi Online" use :)
     - eg: OBJECTS/00000000-00000000-00000000-0000000B/00000000-00000000-00000000-0000000B.BAR
     - eg: OBJECTS/00000000-00000000-00000000-0000000B/00000000-00000000-00000000-0000000B.SHARC
     - Scenes are not affected by this setting as there is no official format for local scene naming (afaik?) 

#### Ignore Timestamp.txt
   - Use this setting to force every archive packed in the current task to take its timestamp from the GUI field above.
   - Only really useful if you plan to use FFFFFFF for everything.
   - This setting simply saves the bother of deleting all the timestamp.txt files.   


<h2 align="center">
   Home Archive Unpacker
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/6d442843-623c-404e-9c67-096ecbed3522)

</div>

### Important Note:
For the Archive Unpacker, utilizing the drag-and-drop functionality from Windows File Explorer is recommended as it has support for adding folders. Upon dragging a folder into the Unpacker drag area, it will recursively scan for any compatible archives and automatically add all of them. 

#### Usage Instructions for Archive Unpacker:
- Drag and drop one or more compatible archives or folders into the tool. It is designed to manage large-scale tasks, such as unpacking 70,000+ objects in a single operation.
- The tool generates a timestamp.txt in the output folder containing the original timestamp of the archive, which should be retained for future repacking of that folder if you dont want to have to edit timestamps field in SDC.
- The default output path for the Archive Unpacker tool is next to the exe in Output/Mapped/. This setting can be modified in the preferences.
- When unpacking objects, if the input archive includes the string "object," then the output folder name will replace this with the UUID (e.g., 00000000-00000000-00000000-0000000B/object_T037.sdat will be extracted to the 00000000-00000000-00000000-0000000B_T037 folder).

#### Options for Archive Unpacker:

##### UUID/Path Prefix:
- A UUID or a complete path prefix can be entered here, which will be appended to any paths identified during the mapping process.
- It will detect the UUID and handle it differently than a normal path prefix.
- Additionally, it will scan for any UUIDs present anywhere in the input file path and attempt to utilize them for mapping.

##### Validate Files:
- This feature is enabled by default. Disabling this option can speed up the mapping process for bulk tasks, although it is not recommended.
- If enabled, this feature will attempt to ensure that all files have been correctly dumped. It employs a combination of header/string byte-level checks and specific libraries to check media files such as mp3, wav, png, jpg.
- It also analyzes xml, json, scene, sdc, odc, etc., searching for indications of corruption, encryption, or compression.
- HomeLuac.exe is used to verify Lua files for syntax errors, which may occasionally result in false flags, as developers sometimes wrote non-standard Lua. However, this check is generally beneficial.
- The tool will also record any 0-byte files for additional inspection. Although this sometimes results in false flags, as developers occasionally used 0-byte files, it is advantageous to flag these for further review.
- Lastly, any items with unmapped files will be logged. This check occurs regardless of whether the validate files feature is enabled. If unmapped files are detected, a _CHECK suffix will be added to the output folder name.
- Should any warnings or file validation failures occur during the validation process, a JobReport.txt will be generated in your output folder.

#### Coredata Mode:
   - This mode skips normal mapping techniques completely as they are not reliable for coredata, instead it uses a preset list of known coredata file names to rename the files.
   - Use this mode for COREDATA.SHARC/BAR, COREOBJECTS.SHARC/BAR, SHADERS.SHARC/BAR, CONFIG***.SHARC/BAR
   - Also works on older builds with varying degrees of success. NPBOOT.BAR, CHARACTERS.BAR, LOCAL_CORE_OBJECTS.BAR, FURNITURE.BAR, DEV_ARCHIBE.BAr, DYNFILES.BAR etc.
   - Bonus: This mode Maps all the 0.41 era scenes pretty much 100%.

#### Bruteforce UUID:
   - This mode is only VERY rarely needed. DO NOT use it normally to map objects it will be slower and due to hash clashes it can sometimes get the UUID wrong. Under normal circumstances the UUID will be somewhere in the input file path either as part of the sdat name, or the folder its in so it will be automatically picked up.
   - This option is only for the rare cases where you have an unknown sdat, such as when there is no inf file available due to being corrupt, or a random source.
   - One bonus of this mode is that it can be used to map sdats directly from raw cache, without having to deinf first.
      - If this mode is used on a CACHE/OBJECTSDEF/ it will rename all the folders from XXXXXX_DAT to UUID_XXXXXX_DAT. 
      - If this mode is used on a CACHE/SCENES/ folder it will rename all the output folders from XXXXXX_DAT to match the scene file + XXXXXX_DAT

##### Extract for Offline:
- This setting affects only the extraction of objects; when enabled, it extracts objects into the "offline" folder structure with all files and folders at the root level. This configuration is ideal for running in extracted form on HDK builds.
- This was the standard practice until recently. If you have previously extracted objects using older tools like Gaz's HomeTool, you will be familiar with this structure. However, it is not the correct folder structure needed for rebuilding archives, so caution is advised. Only enable this option if you dont intend to repack the objects into archives.
- Given the revival of Online, this is no longer the default folder structure required, It is important to note that unlike other settings, this one does not persist between sessions.

#### Compatibility Patches:
 - This does some patches on the fly that allow newer items to work on older clients.
   - Patche 1: Every 4th byte of every MDL gets patched from 04 to 03. This allows older clients such as 1.00 to load newer MDL files.
   - Patch 2: Changes .SCENE files to use file:// links instead of file:///
   - More might be added here as they are discovered.
  
#### Delete File.txt/Manifest
 - This deletes these 2 extra custom files that are created during the packing proccess and can help with mapping.
 - These are not really needed once the item is mapped.
 - If you recreate the archive these will be recreated inside it again.
 - Enabling this option just means you wont ever see them, but they will still be there when needed

<div align="center">


</div>

<div align="center">


</div>


<h1 align="center">
   Tab 2: CDS Tool 
</h1>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/eb18af83-a54d-4c64-8619-0964fb32f243)



</div>

This tab handles all the smaller xml files that are encrypted with their SHA1. 99.9% of the time you dont need to supply any SHA1 as it can use another method to get the IV, however in some rare cases where the file has a non standard header you might need to supply a SHA1 to decrypt. 

<h2 align="center">
   CDS Encrypter Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/c8223ead-645b-4524-a011-1858d07ab816)

</div>

### Note:
- The CDS encrypter tool will automatically generate the SHA1 for input files and then use the first 16 bytes of that SHA1 to encrypt the file.
- CDS Encrypter tool by default will output to Output/CDSEncrypt/ next to the exe. Change this in settings.

#### Options:
- Append SHA1 to filenames: Append the original SHA1 to the output filenames. This essentially means the decryption key is attached to the file.
- Rename for CDN: If input files are named like UUID.odc or UUID_TXXX.odc this will rename them to suit CDN format (eg. Objects/9178D77B-417940EC-9BA99895-B1CA1179/object_T045.odc)

<h2 align="center">
   CDS Decrypter Tool
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/1ba691f4-a95e-4a04-b5c2-73af5676f207)

</div>

### Note:
- CDS Decrypter Tool can decrypt almost all ODC / SDC / SceneList xmls wihout any SHA1 provided. It uses an exploit to "work backwards" and "guess" the IV used.
- This exploit will work as long as the header of the xml has one of several expected byte sequences. 
- In rare cases where there is a modified header you will need to supply a sha1 for decryption, this can be supplied either in the filename, or typed into SHA1 input box.
- If any SHA1 is typed into input box it will override the SHA1 found in filename if one exists.
- CDS Encrypter tool by default will output to Output/CDSDecrypt/ next to the exe.


<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 3: HCDB Encrypter / Decrypter
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/6028c426-f9b7-4ce9-8041-f507176f60a5)



</div>

### Notes:
- Playstation Home HCDB object catalogues are natively SQL files that are LMZA compressed to a segs file which reduces the filesize by about 80%.
- Then that segs file (compressed SQL) is encrypted in a similar way to SDC/ODC with the first 16 bytes of the segs SHA1 used as the encryption IV.
- Currently you DO need to supply the segs files SHA1 to decrypt. In later versions it will be able to brute force the IV as most bytes of the segs file header are known.


</div>

<h1 align="center">
   Tab 4: SceneID Generator / Decrypter
</h1>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/94ebe836-6b6a-4569-9944-d4c415ef77bb)



</div>

### Note:
Scene IDs, also known as Channel IDs, are critical for instancing in PlayStation Home. If two scenes share a Scene ID listed in SceneList.xml, players will be placed into the same instance. Generally, each scene must have a unique Scene ID, although under certain conditions where scenes are VERY similar, sharing IDs may be feasible.

<h2 align="center">
  Scene ID Generator
</h2>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/f436ec03-fdc3-4b4a-9f5e-70fafdbbc1aa)

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
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/b74a8359-684d-43ab-8bdc-f9325676c8dc)

</div>

#### Usage:
- Enter one or more Scene IDs (separated by spaces, commas, or lines).
- You can also drag a plaintext SceneList.xml into the right-hand side of the tab, and it will parse and decrypt all IDs. Ensure not to drag the XML directly onto the textbox.

#### Options:
- Legacy Mode, when activated, facilitates the decryption of early type IDs suitable for earlier Home builds.
- Generally, this setting does not require modification; keep it on default (Disabled) for newer Home versions.


<div align="center">


</div>

<div align="center">


</div>


<h1 align="center">
   Tab 5: LUA / LUAC TOOL
</h1>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0915629a-b85b-4a15-b993-f237f39f85a5)



</div>

<h2 align="center">
  Home LUA Compiler
</h2>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/9ef4a655-2dea-4606-8d44-4fa7c85ddb6b)

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
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/508fcbe9-aac7-41f6-a122-57efdaaa34ff)

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


<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 6: SDC / ODC Tool
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/a37847e9-5f67-4da3-83e1-3b424e930e0c)

</div>

<div align="center">

### Use this tab to Create Plaintext or Encrypted SDC and ODC files.

</div>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/b5be8545-46af-4356-b3cc-854b37537ca6)

</div>

### SDC Creator:

- SDCs are small XML based files used for storing scene information.
- Usage: Fill out the fields and click create. If you want to create an encrypted SDC in one step, enable the toggle switch.
- Whether you choose to encrypt it or not, The SHA1 of the plaintext file will be shown when you click create. This is what becomes the encryption key.
- Choose Offline mode to exclude the archive element from the SDC. These offline SDCs in plaintext form are suitable for using with HDK builds and local files so no archive download needed.
- The created file will have the name autofilled to match the sdat name in the archive element if there is one. 
- The SDC Content box will show the generated SDC for review.
- Use the clear button to revert all fields to default before creating a new SDC.

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/94eb9854-3c6a-4a38-a48a-fa539b3f97eb)

</div>
### ODC Creator:

- ODCs are small XML based files used for storing object information.
- Usage: Fill out the fields and click create. If you want to create an encrypted ODC in one step, enable the toggle switch.
- Whether you choose to encrypt it or not, The SHA1 of the plaintext file will be shown when you click create. This is what becomes the encryption key.
- Generate random UUIDs here with one click. The chances of generating a clashing UUID are extremely small. Not worth worrying about IMO.
- The created file will have the name autofilled to UUID.odc or UUID_TXXX.odc if you included a version.
- The ODC Content box will show the generated ODC for review.
- Use the clear button to revert all fields to default before creating a new ODC.


<h1 align="center">
   Tab 7: Path2Hash Tool
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/79c8f10e-f22b-4975-a804-e16eded39d8d)



</div>

Use this tab for debugging mapping issues. It allows you to attempt to discover the paths to unmapped files.

If you know the path to a file and it refuses to map normally, you can add the path here by clicking "Add to Mapper". Once added if this file is ever encountered again it will map automatically.

<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 8: Home EBOOT Patcher
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/900dba64-b5e5-491b-aa45-acf0b9201a95)



</div>

View and or Patch various fields in Home EBOOTS. 

### Work in Progress

<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 9: Checker
</h1>

<div align="center">
   
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/a95cad13-cdbb-448e-89fe-5f61f65cf6b8)



</div>

SHA1 Checker: Used for checking SHA1s and building lists for comparison

Validate Files: This is a standalone version of the file validator used in the mapper tool. Use this tool on already mapped content. 

Get File Details: New tool that shows file info - Work in Progress

This tab is currently not working fully. Just the sha1 checker portion works


<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 10: Content Catalogue
</h1>

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/7ef1604a-858b-42fb-8b92-2c38abdd4dbb)




#### Catalogue Usage
- New feature allow the user to store 5 managed lists - Right click any item to manage them:
  - Add/Remove from list 1, 2, 3, 4, 5
  - Add to all Lists
  - Remove from All lists 
- These lists can be browsed like the other categories, this allows you to visualize your postinstall.sql items.
- Right click on the list icons, 1, 2, 3, 4, 5 to show the new options:
  - Push Direct to PS3: Push any list to your PS3 instantly. Requires you to have FTP running on your PS3 like webman mod, and your PS3 FTP IP must be set correctly in settings.
  - Push Direct to RPCS3: Push any list to your RPCS3 client instantly. Make sure to set your RPCS3 dev_hdd0 path in settings first. 
  - Save List as PKG: Create a custom PKG with one click. Install on either PS3 or RPCS3
  - Save List as SQL: Like PostInstall Tool 2.0, this just lets you save a SQL.
  - Upload SQL/XML/TXT: Use this option to upload and visualize, edit, resave a previously created SQL or any file containing UUIDs.
    - You can even load a scene file here to see what minigames it uses. 
  - Clear this List: Clear this list completely

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/30ab6577-bc39-447a-8de0-0f7c83ad675b)



<h1 align="center">
   Tab 11: Media Tool
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/d72e4f73-abaf-4387-87ae-1e698f1b2229)



</div>

### Video Converter Usage:
- Local Videos: Drag and drop videos into drag area, or Click to browse and choose files.  
- Youtube conversion: Enter URL, or multiple URLs (comma, space, line or pipe separated)
- Choose settings and click convert. Find output in Output/Video when its finished
- Video Quality: 576p recommended for Home for best balance between filesize and quality.
- Recommended to choose 4:3 when converting 4:3 videos as it will result in higher quality in the end, the defult setting is 16:9.
- If you ignore this aspect ratio setting it will still work ok in home as Home stretches/squashes videos to fit the screen they are shown on anyway.
- Audio bitrate, 160kbps recommended for best balance between filesize and quality. 
- For situations where the input video has low audio levels, you can choose +3DB or +6DB audio boost. Toggle on both for a +9DB boost in extreme cases.


### BNK Unpacker
- Many Home assets used Sonys BNK format to store sound effects. This tool extracts them to WAV. 
- Basic initial support only at this stage. MP3 mode WIP.

<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 12: Settings
</h1>

<div align="center">

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/f2d4ce66-a0d6-4e99-b1e1-caac5f55bca7)



</div>

Set your output paths and various other options here. 

At first boot, a settings.xml is created next to the exe. This also sets all output paths next to the exe in Outputs folder. 

Debug Logs: If this is enabled you will find detailed logs of all operations performed in logs/debug.log next to the exe. 

For the catalogue:
- If you want to be able to send POSTINSTALL.SQL files to your RPCS3 client instantly, Set your RPCS3 dev_hdd0 folder here.
- If you want to be able to send POSTINSTALL.SQL files to your PS3 client instantly, Set your PS3 IP Address here.
- If you are using a title ID other than the standard NPIA00005 change it here. Applys to both RPCS3 and PS3.
- Catalogue shows only usable. This setting when enabled hides the catalogue items that should NOT be added to user inventory.
  - Bundles
  - Minigames
  - System
 - Disable this option if you want to browse and search all UUIDs.

