# Multiserver 3 - Nautilus Fork

This is a fork of AgentDarks447's awesome server project Multiserver3. This fork is specifically aimed at tweaking the Nautilus plugin and trying out experimental changes which might not be 100% stable or could cause issues with web tool portion of multiserver. If you want to use multiserver as a game server I recommend the official version that can be found here https://github.com/GitHubProUser67/MultiServer3  

### USING NAUTILUS IS ALWAYS AT YOUR OWN RISK! RECOMMENDED TO BACKUP YOUR DATA FIRST!


 
# Tab 1: BAR/SDAT/SHARC TOOL
![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0a378bb7-382a-4ff6-b328-a7fff8cb836c)


# Tool 1: Home Archive Creator:

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

### Note: For Archive Creator input it's currently recommended to use drag and drop from windows file explorer as that can handle folders or zip files. Click to browse function only supports zips currently.

## Archive Creator Usage: 
- Drag in one or more folders and choose archive type before clicking Create. It should be able to handle large tasks such as 60k objects in one operation.
- Enter a 8 byte timestamp to match timestamp field in your SDC if needed.
- Default output path for the archive creator tool is next to the exe/Output/Archives/. This can be changed in settings but will reset to default on next session


## Archive Creator Options

## Timestamp:
   - Add timestamp here. Default is FFFFFFFF. If less than 8 bytes are entered it will be padded to 8 bytes with prefix 0.
   - If timestamp.txt exists in your input folder this GUI field will be ignored.

## Archive Types:
- BAR:
    - Simplest form of Home Archive used in early retail home and later only used for developer versions of Home.
    - Although superceded by sharc this format is supported in retail clients up to 1.86.
    - On later 1.82+ clients BAR archives can be blocked from mounting in online mode by including the disablebar flag in the TSS
    - Due to no extra security layers on these, just simple zlib compression. These are the fastest to read/mount/create/dump.
    - I recommended using this setting for scenes in general. Objects need to be sdat (NPD encrypted) on later retail versions.
    - Interestingly even on 1.86 retail you can use this type of archive to replace COREDATA.SHARC and even disablebar flag in TSS can not block that.
  
- BAR Secure:
   - Encrypted BAR Used in conjunction with SHA1 in TSS in some earlier pre sharc versions of home.
   - Only used for the older style xml based object catalogue and possibly for config bar too.
   - Generally not really needed or used on 1.8x clients but there is still has support afaik

- SDAT:
   - These are the same as BAR but with NPD encryption layer applied on top.
   - Very fast to read too - Not much different to BAR as NPD layer is handled by the firmware on the fly
   - This was the format generally used by retail Home from version 1.00 up to 1.81

- SDAT SHARC:
    - Secure archive format brought in with version 1.82 to prevent hacking and piracy.
    - Not really much point using this format anymore since AgentDark447 cracked it and released tools.

- CORE SHARC:
    - Format introduced with v1.82+. Used to secure local COREDATA sharc files in the client pkg.
    - Not really much point using this format anymore since AgentDark447 cracked it and released tools.

- Config SHARC:
    - These are used for online mode configuration files pushed to client during initial connection.
    - Retail 1.82+ needs these to be encrypted with this setting.
    - These are different than other sharc files in that these are secured with content server key. 
    - These are the only type of sharc that is still needed for Retail online in 2024. 




# Tool 2: Home Archive Unpacker

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)


### Note: For Archive Unpacker input it's currently recommended to use drag and drop from windows file explorer as that can handle folders. When you drag a folder into Unpacker it will scan it recursively for any compatible archives and add all. 

## Archive Unpacker Usage: 
- Drag in one or more compatible archives or folders. It should be able to handle large tasks such as unpacking 60k objects in one operation.
- It will create a timestamp.txt in the output folder with the original timestamp of the archive, leave this in place and it will be used during future repacking of that same folder.
- Default output path for the archive Unpacker tool is next to the exe/Output/Mapped/. This can be changed in settings but will reset to default on next session.
- When unpacking objects, if the input archive has the string "object" in it, then the output folder will have it replaced with the UUID (eg. 00000000-00000000-00000000-0000000B/object_T037.sdat will extract to 00000000-00000000-00000000-0000000B_T037 folder)



## Archive Unpacker Options:

## UUID/Path Prefix:
 - You can enter a UUID, OR a full path prefix here that will be added onto any paths found during the mapping process.
 - Note: It will also scan for any UUIDs ANYWHERE in input file path and attempt to use those for mapping too.  

## Validate files:
  - Enabled by default. Not recommended to disable this option but the mapper will be faster on bulk tasks if you do.
  - If enabled this option will make an attempt to validate that all files have dumped correctly. it uses combination of header/string byte level checks and dedicated libaries for checking media files such as mp3/wav/png/jpg
  - It will also parse xml/json/scene/sdc/odc etc looking for bytes indicating corruption/encryption/compression.
  - It uses HomeLuac.exe to parse lua files for syntax errors. This can lead to some false flags as home devs did sometimes write non valid lua but overall its useful. 
  - It will also log any 0 byte files for further checking, again this does lead to some false flags due to the fact Home devs did sometimes use 0 byte files but overall its useful ot have them flagged for further checking
  - Finally it will log any items with unmapped files - this check is slightly different as it always happens regardless if validate files is enabled or not. If any unmapped files are detected it will add _CHECK suffix to output folder name.
  - If any warnings (file validation failures) are detected at all during validation you will find a JobReport.txt in your output folder.


## Offline Structure:
  - This setting only effects object extraction, when enabled it extracts objects into the "offline" folder structure with all files and folders in root. This is perfect for running in extracted form on HDK builds.
  - This was the norm up until recently and if you ever extracted objects with older tools such as gazs HomeTool you will be familiar with it, but its not the true folder structure needed for rebuilding archives so use with caution.
  - Now that Online is revived this is no longer the default folder structure needed so its not the default setting. Note unlike other settings, This setting does not get remembered between sessions.


# Tab 2: SceneID Generator / Decrypter

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4204a35c-9dea-40d5-afed-5367d5e8fb75)

# Tool 1: Scene ID Generator

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fbc2f728-4c22-4f3d-9c2f-00091be53052)

Note: Scene IDs aka Channel IDs are very important to instancing in playstation home. If 2 scenes share a Scene ID in SceneList.xml then players will be put into same instance.  
This means generally each scene must have a unique Scene ID but in some cases where scenes are similar enough it might be possible to share IDs.


## Usage
- Enter a number between 1 and 65535, Click Encrypt
- Also accepts hyphen separated ranges for bulk generation

## Options
- Generally you dont need to touch this setting, leave it on default (Disabled) for newer Home.
- Legacy Mode when enabled allows for generation of early type IDs to suit earlier home builds.


# Tool 2: Scene ID Decrypter

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4e5679fa-3fe0-4cf4-b393-651b15a7384c)

Note: Scene IDs aka Channel IDs are very important to instancing in playstation home. If 2 scenes share a Scene ID in SceneList.xml then players will be put into same instance.  
This means generally each scene must have a unique Scene ID but in some cases where scenes are similar enough it might be possible to share IDs.


## Usage
- Enter one or more Scene IDs (space, comma line separated)
- You can also *drag a plaintext SceneList.xml into the right hand side of the tab and it will parse it and decrypt all IDs.
   Dont drag the xml exactly onto the textbox

 
## Options
- Generally you dont need to touch this setting, leave it on default (Disabled) for newer Home.
- Legacy Mode when enabled allows for decryption of early type IDs to suit earlier home builds.
