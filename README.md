<h1 align="center">
   Nautilus Toolset v1.02
</h1>

<h2 align="center">
   
Download Latest Compiled Release [HERE](https://github.com/DeViL303/MultiServer3-NautilusFork/releases/download/Release_1.02/Nautilus_Toolset_v1.02.zip)

</h2>

<h1 align="center">
   Nautilus Fork by Home Laboratory
   </h1>

This repository is a place holder with information on Nautilus. For the source code of Nautilus and Multiserver the official repo is available [HERE](https://github.com/GitHubProUser67/MultiServer3). 

This application is the product of the PlayStation Home research and testing done by Home Laboratory team members over the last few years. 

Also Credits to the developers of UnluacNET, Unluac jar's, scetool, HomeLuac :), create_pkg, vgmstream. More detailed credits to come. 

<h1 align="center">
   What is Nautilus?
   </h1>
   
Nautilus is a Windows GUI designed to work on Playstation Home assets on a deeper level than has ever been possible before. The end goal is to have an All-in-One solution for automating common tasks and dealing with the many custom file types used in Home. 

Note: Due to the nature of Homes millions of assets and the many edge cases involved this application will most likely never be "finished".

### Important: Use of Nautilus is entirely at your own risk! It is recommended to backup your data beforehand!

<div align="center">


</div>


<h1 align="center">
   Tab 1: BAR/SDAT/SHARC TOOL
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/9446cdef-d6b8-453f-b357-863cd4aef538)







</div>

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
- **CORE SHARC:** First introduced in version 1.82+, this format secures local COREDATA sharc files within the client package. These are encrypted with a local key that is built into 1.82+ Retail EBOOTS (Also Online Debug aka QA EBOOTs).
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
   - Use this mode for 1.8x COREDATA.SHARC/BAR, CORE_OBJECTS.SHARC, SHADERS.SHARC/BAR, LOCAL_CORE_OBJECTS.BAR, CONFIG***.SHARC/BAR
   - Also works on older builds with varying degrees of success. NPBOOT.BAR, CHARACTERS.BAR, LOCAL_CORE_OBJECTS.BAR, FURNITURE.BAR, DEV_ARCHIVE.BAR, DYNFILES.BAR etc.
   - Bonus: This mode Maps all the 0.41 "GDC" era scenes and coredata pretty much 100% (Not including 0.41 Arcade BARs which are currently not mappable)

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

#### Repack All
- This mode sends each unpacked folder directly into the repacker tab and repacks it with the chosen settings.
- Make sure to choose the correct repack format BEFORE starting the unpack process.
- This will act like a converter - eg bulk repack sdat to bar, or sharc type sdats to normal for HDK builds.
  
<div align="center">


</div>

<div align="center">


</div>


<h1 align="center">
   Tab 2: CDS Tool 
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/4b9b5a10-9d5b-4c39-a197-17ec8abe2dfb)





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

![image](https://github.com/user-attachments/assets/7b229e81-715b-4bbb-9be9-192d1bccaa72)





</div>

### Notes:
- Playstation Home HCDB object catalogues are natively SQL files that are LMZA compressed to a segs file which reduces the filesize by about 80%.
- Then that segs file (compressed SQL) is encrypted in a similar way to SDC/ODC with the first 16 bytes of the segs SHA1 used as the encryption IV.
- Currently you DO need to supply the segs files SHA1 to decrypt. In later versions it will be able to brute force the IV as most bytes of the segs file header are known.


</div>

<h1 align="center">
   Tab 4: DB Editor
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/881de220-2fca-4309-aeb0-49ba423fce64)




</div>

### Features:
- Open PS Home Object Catalogue SQL files and Add/Remove items
- Import SQL, Export to old style XML which is stil supported in new versions. It not known if there is a limit on item count in XML style catalogue.
- Shows plaintext hex timestamp and sha1 in GUI so its easier to read and edit data, No need to worry about converting data types anymore.
- It will convert data entered to int and 20 byte blob or whatever is needed when you click "Add Item". 
- Edit existing sha1, timestamp, version, keyname, value fields and simply press enter to save changes back to SQL instantly. 
- Export to 4 x HCDB, one for each region when finished editing SQL (Gives you SHA1 for TSS when you export to HCDB)
- Jump to and load any UUID or index, to load a range of indexes put in 2 values hyphen separated like 71345-71435). Loading more than 100 at a time will make GUI slow.
- Delete any item or range of items in same way as loading, enter hyphen separated values, it will reorder all remaining items to suit.
- Adding items - By default it will autofill next index, If you override this with existing UUID or index, if it does exist already it will ask if you want to replace the item.
- Verifies all fields are correct format, that UUID is 8-8-8-8, SHA1 is 20bytes etc 
- Easy add option, Drag in ODC, plaintext or encrypted and it will autofill out UUID, Timestamp, Version, SHA1 fields.
  
</div>


#### DB Editor Usage
- First you will need to Decrypt your HCDB to SQL once with HCDB tab (SHA1 Required)
- Then in DB editor Tab open the SQL - You can keep editing and repacking this same SQL from now on.
- To add an item, Drag in the ODC. It does not matter if its encrypted or plaintext. This will set all top row fields correctly.
- Alternatively you can enter in top row details manually.
  - The Fields with NULL do not need to be touched normally, just enter your new items UUID, Version and SHA1.
  - The Object Index should automatically get set to the next available index when you load an SQL.
  - When you click Add Item the Object Index will increment by 1 to the next available index.
  - Note: If needed double clicking the UUID textbox on top row will generate a random UUID.   
- Once top row fields are set, then choose type in first keyname dropdown such as CLOTHING, FURNITURE, MINIGAME etc.
- This will fill out all other required fields with default values making it easy to see what needs to be set for each type.
- Choose subtype in first Value dropdown, CHAIR, HAIR etc. Subtype Options will change depending on which Keyname was chosen.
- For entitlement it sets CLOTHING and FURNITURE as LUA_REWARD by default.
- Note: IF you try to add an item with an existing object index or UUID, it will ask if you want to replace the existing item. 
- When you have set the dropdown menus click Add Item. If you have more items to add repeat the above.
- When all items have been added, click Export HCDB, it will create all 4 HCDB files and provide the segs SHA1 aka HCDB decryption key (Needed for TSS) 
 


### Bulk Metadata Editor
- Add new Metadata fields to all items easily
- Simple Find and Replace for Metadata values
- Add metadata based on matching an existing metadata keyname or value.
- Allows applying some experimental patches to all items


<div align="center">

![image](https://github.com/user-attachments/assets/b0fa4b8e-bb0c-4651-a912-70a9b7352a3c)


</div>

<h1 align="center">
   Tab 5: SceneList Viewer/Editor
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/8793fbb2-a34c-4ec5-a9b5-ee5b9f8a05df)

</div>

<h1 align="center">
   Tab 6: TSS Viewer/Editor
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/2310ea31-f1c0-4537-a6da-99944aa54d90)


</div>

<h1 align="center">
   Tab 7: SceneID Generator / Decrypter
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/f5bcced2-cd9c-4cad-900a-d5fa552ac05b)


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
   Tab 8: LUA / LUAC TOOL
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/a9cc8362-f5b3-4b16-9c59-51e49ce6fecf)


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
   Tab 9: SDC / ODC Tool
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/cec3301a-b0bb-4e4d-93f5-6e0abaa1c53b)


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
   Tab 10: Path2Hash Tool
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/727bcb69-f252-4120-9473-bf39bcdf8ffc)



</div>

Use this tab for debugging mapping issues. It allows you to attempt to discover the paths to unmapped files.

If you know the path to a file and it refuses to map normally, you can add the path here by clicking "Add to Mapper". Once added if this file is ever encountered again it will map automatically.

<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 11: Home EBOOT Information Viewer / Patcher
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/227e085f-db12-4243-8a57-5fc358aaaff6)





</div>

View and or Patch various fields in Home EBOOTS. 

### Work in Progress

<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 12: Checker
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/a3727b73-fdc9-49dc-b1f0-f175a5508d2d)


</div>

SHA1 Checker: Used for checking SHA1s and building lists for comparison

Validate Files: This is a standalone version of the file validator used in the mapper tool. Use this tool on already mapped content. Drag in files or folders to run through validator.
- This uses a combination of header checks, byte sequence checks, and loading media files with specific libaries, mostly to confirm files are decrypted correctly.  


<div align="center">


</div>

<div align="center">


</div>

<h1 align="center">
   Tab 13: Content Catalogue
</h1>

<div align="center">
   
![image](https://github.com/user-attachments/assets/023c2af4-6441-478e-a9f7-4b85ce7f00dc)


</div>


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
   Tab 14: Media Tool
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/593fa44e-961f-428c-beb6-edf1f14bdd72)




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
   Tab 15: Settings
</h1>

<div align="center">

![image](https://github.com/user-attachments/assets/91aa867c-0839-41da-a1dd-e0ea068a5fbf)


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


### Integration between tabs 
- v1.02 brings more advanced features to try reduce the steps involved in working on CDN files
- Some examples of this are:
  - Create an ODC and its SHA1 and UUID get auto added to DB editor ready for the next step which is usually adding that item to the SQL.
  - Create an SDC and its SHA1 gets auto added to scenelist editor and a new SceneID is created ready for the next step which is usually adding that item to the SceneList.xml
  - Edit the Scenelist or the HCDB and their SHA1s are auto added to TSS editor, with the files held in a temporary folder until ready to be deployed.
  - When ready all changed files and the new TSS files can be deployed to the live CDN with one click. 
