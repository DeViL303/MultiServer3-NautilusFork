# Multiserver 3 - Nautilus Fork

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0a378bb7-382a-4ff6-b328-a7fff8cb836c)

This is a fork of AgentDarks447's awesome project Multiserver3. This fork is aimed at tweaking the Nautilus plugin and trying out experimental changes which might not be 100% stable or could cause issues with web tool portion of multiserver.  

 
Tab 1 - Tool 1: BAR/SDAT/SHARC - Home Archive Creator

Note: For input it's currently recommended to use drag and drop from windows file explorer as that can handle folders or zip files. Click to browse function only supports zips currently.

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

Usage: 
- Drag in one or more folders and choose archive type before clicking Create. It should eb able to handle large tasks such as 10k objects in one operation
- Enter 8 byte timestamp to match SDC if needed. If there is a valid timestamp.txt found in the input folder it will use that timestamp instead.
- Default output path for the archive creator tool is next to the exe/Output/Archives/. This can be changed in settings

Archive Types:
- BAR:
    Simplest form of Home Archive used in early retail home and later only used for developer versions of Home.
    Although superceded by sharc this format is supported in retail clients up to 1.86.
    On later 1.82+ clients BAR archives can be blocked from mounting in online mode by including the flag <disablebar/> in the TSS
    Due to no extra security layers on these, just simple zlib compression. These are the fastest to read/mount/create/dump.
    I recommended using this setting for scenes in general. Objects need to be sdat (NPD encrypted) on later retail versions.
    Interestingly even on 1.86 retail you can use this type of archice to replace COREDATA.SHARC and even <disablebar/> flag in tss can not block that.
  
- BAR Secure:
   Encrypted BAR Used in conjunction with SHA1 in TSS in some earlier pre sharc versions of home.
   Only used for the older style xml based object catalogue and possibly for config bar too.
   Generally not really needed or used on 1.8x clients but there is still has support afaik

- SDAT
   These are the same as BAR but with NPD encryption layer applied on top.
   Very fast to read too - Not much different to BAR as NPD layer is handled by the firmware on the fly
   This was the format generally used by retail Home from version 1.00 up to 1.81

- SDAT SHARC
    Secure archive format brought in with version 1.82 to prevent hacking and piracy.
    Not really much point using this format anymore since AgentDark447 cracked it and released tools.

- CORE SHARC
    Format used for local COREDATA sharc files in the client pkg.
    Not really much point using this format anymore since AgentDark447 cracked it and released tools.

- Config SHARC
    These are used for online mode configuration files pushed to client during initial connection.
    Retail 1.82+ needs these to be encrypted with this setting.
    These are the only type of sharc that is still needed in 2024

  
   
Tab 1 - Tool 2: BAR/SDAT/SHARC - Home Archive Unpacker

Note: For input it's currently recommended to use drag and drop from windows file explorer as that can handle folders. When you drag a folder into Unpacker it will scan it recursivelt for any compatible files and add each one. 

![image](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)


Usage: 
- Drag in one or more compatible archives or folders. It should be able to handle large tasks such as unpacking 10k objects in one operation.
- It will create a timestamp.txt in the output folder with the original timestamp of the archive, leave this in place and it will be used during future repacking of that same folder.
- Default output path for the archive Unpacker tool is next to the exe/Output/Mapped/. This can be changed in settings.
