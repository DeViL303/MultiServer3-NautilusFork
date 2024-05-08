# Multiserver 3 - Nautilus Fork

This is a fork of AgentDarks447's awesome server project Multiserver3. This fork is specifically aimed at tweaking the Nautilus plugin and testing experimental changes which might not be 100% stable or could cause issues with the web tool portion of Multiserver. If you want to use Multiserver as a game server, I recommend the official version that can be found [here](https://github.com/GitHubProUser67/MultiServer3).

### USING NAUTILUS IS ALWAYS AT YOUR OWN RISK! RECOMMENDED TO BACKUP YOUR DATA FIRST!

## Tab 1: BAR/SDAT/SHARC TOOL

<div align="center">

![BAR/SDAT/SHARC Tool](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0a378bb7-382a-4ff6-b328-a7fff8cb836c)

</div>

## Tool 1: Home Archive Creator

<div align="center">

![Home Archive Creator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

</div>

### Note
For Archive Creator input, it's currently recommended to use drag and drop from Windows File Explorer as that can handle folders or zip files. The "click to browse" function currently only supports zip files.

#### Archive Creator Usage:
- Drag in one or more folders and choose the archive type before clicking "Create." It should be able to handle large tasks such as 60k objects in one operation.
- Enter an 8-byte timestamp to match the timestamp field in your SDC if needed.
- Default output path for the archive creator tool is next to the exe/Output/Archives/. This can be changed in settings but will reset to default on the next session.

#### Archive Creator Options:

##### Timestamp:
   - Add timestamp here. Default is FFFFFFFF. If less than 8 bytes are entered, it will be padded to 8 bytes with a prefix of 0.
   - If a timestamp.txt exists in your input folder, this GUI field will be ignored.

##### Archive Types:
- **BAR:** Simplest form of Home Archive used in early retail home and later only used for developer versions of Home. These are the fastest to read/mount/create/dump due to no extra security layers, just simple zlib compression.
- **BAR Secure:** Encrypted BAR used in conjunction with SHA1 in TSS in some earlier pre-sharc versions of home.
- **SDAT:** Similar to BAR but with an NPD encryption layer applied on top.
- **SDAT SHARC:** Secure archive format introduced with version 1.82 to prevent hacking and piracy.
- **CORE SHARC:** Introduced with v1.82+ to secure local COREDATA sharc files in the client pkg.
- **Config SHARC:** Used for online mode configuration files pushed to the client during initial connection.

## Tool 2: Home Archive Unpacker

<div align="center">

![Home Archive Unpacker](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)

</div>

### Note:
For Archive Unpacker input, it's currently recommended to use drag and drop from Windows File Explorer as it can handle folders. When you drag a folder into the Unpacker, it will scan it recursively for any compatible archives and add all.

#### Archive Unpacker Usage:
- Drag in one or more compatible archives or folders. It should be able to handle large tasks such as unpacking 60k objects in one operation.
- It will create a timestamp.txt in the output folder with the original timestamp of the archive; leave this in place, and it will be used during future repacking of that same folder.
- Default output path for the archive Unpacker tool is next to the exe/Output/Mapped/. This can be changed in settings but will reset to default on next session.
- When unpacking objects, if the input archive has the string "object" in it, then the output folder will have it replaced with the UUID (e.g., 00000000-00000000-00000000-0000000B/object_T037.sdat will extract to 00000000-00000000-00000000-0000000B_T037 folder).

#### Archive Unpacker Options:

##### UUID/Path Prefix:
- You can enter a UUID or a full path prefix here that will be added onto any paths found during the mapping process.
- Note: It will also scan for any UUIDs ANYWHERE in the input file path and attempt to use those for mapping.

##### Validate Files:
- Enabled by default. It is not recommended to disable this option, but the mapper will be faster on bulk tasks if you do.
- If enabled, this option will make an attempt to validate that all files have dumped correctly. It uses a combination of header/string byte-level checks and dedicated libraries for checking media files such as mp3/wav/png/jpg.
- It will also parse xml/json/scene/sdc/odc, etc., looking for bytes indicating corruption/encryption/compression.
- It uses HomeLuac.exe to parse Lua files for syntax errors. This can lead to some false flags as home devs did sometimes write non-valid Lua, but overall it's useful.
- It will also log any 0-byte files for further checking; again, this does lead to some false flags due to the fact Home devs did sometimes use 0-byte files, but overall it's useful to have them flagged for further checking.
- Finally, it will log any items with unmapped files—this check is slightly different as it always happens regardless of whether validate files are enabled or not. If any unmapped files are detected, it will add a _CHECK suffix to the output folder name.
- If any warnings (file validation failures) are detected at all during validation, you will find a JobReport.txt in your output folder.

##### Offline Structure:
- This setting only affects object extraction; when enabled, it extracts objects into the "offline" folder structure with all files and folders in the root. This is perfect for running in extracted form on HDK builds.
- This was the norm up until recently, and if you ever extracted objects with older tools such as Gaz's HomeTool, you will be familiar with it, but it's not the true folder structure needed for rebuilding archives, so use with caution.
- Now that Online is revived, this is no longer the default folder structure needed, so it's not the default setting. Note, unlike other settings, this setting does not get remembered between sessions.

## Tab 2: SceneID Generator / Decrypter

<div align="center">

![SceneID Generator / Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4204a35c-9dea-40d5-afed-5367d5e8fb75)

</div>

## Tool 1: Scene ID Generator

<div align="center">

![Scene ID Generator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fbc2f728-4c22-4f3d-9c2f-00091be53052)

</div>

### Note:
Scene IDs, also known as Channel IDs, are very important for instancing in PlayStation Home. If two scenes share a Scene ID in SceneList.xml, then players will be put into the same instance. This means generally each scene must have a unique Scene ID, but in some cases where scenes are similar enough, it might be possible to share IDs.

#### Usage
- Enter a number between 1 and 65535, click Encrypt.
- Also accepts hyphen-separated ranges for bulk generation.

#### Options
- Generally, you don't need to touch this setting; leave it on default (Disabled) for newer Home.
- Legacy Mode, when enabled, allows for the generation of early type IDs to suit earlier home builds.

## Tool 2: Scene ID Decrypter

<div align="center">

![Scene ID Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4e5679fa-3fe0-4cf4-b393-651b15a7384c)

</div>

### Note:
As mentioned earlier, Scene IDs are crucial for instancing in PlayStation Home. Sharing IDs between similar scenes is possible but should be handled with care.

#### Usage
- Enter one or more Scene IDs (space, comma, line-separated).
- You can also drag a plaintext SceneList.xml into the right-hand side of the tab, and it will parse and decrypt all IDs. Don't drag the xml exactly onto the textbox.

#### Options
- Generally, you don't need to touch this setting; leave it on default (Disabled) for newer Home.
- Legacy Mode, when enabled, allows for decryption of early type IDs to suit earlier home builds.
