# Multiserver 3 - Nautilus Fork

This repository is a fork of AgentDarks447's acclaimed project, Multiserver3, focusing on modifications to the Nautilus plugin and testing experimental features, which may not be entirely stable and could affect the web tool component of Multiserver. For gaming server use, the official version is recommended and can be found [here](https://github.com/GitHubProUser67/MultiServer3).

### Caution: Use of Nautilus is at your own risk. It is highly recommended to backup your data before proceeding!

## Tools Overview

### Tab 1: BAR/SDAT/SHARC Tool

<div align="center">

![BAR/SDAT/SHARC Tool](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0a378bb7-382a-4ff6-b328-a7fff8cb836c)

</div>

### Tool 1: Home Archive Creator

<div align="center">

![Home Archive Creator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/297ac8dc-65c2-4056-a4b8-8de8fcc07085)

</div>

#### Usage
- Drag in one or more folders and choose archive type before clicking Create. It should be able to handle large tasks such as 60k objects in one operation.
- Enter a 8 byte timestamp to match timestamp field in your SDC if needed.
- Default output path for the archive creator tool is next to the exe/Output/Archives/. This can be changed in settings but will reset to default on next session.

#### Archive Types
- **BAR:** Simplest form of Home Archive used in early retail home and later only used for developer versions of Home.
- **BAR Secure:** Encrypted BAR Used in conjunction with SHA1 in TSS in some earlier pre sharc versions of home.
- **SDAT:** These are the same as BAR but with NPD encryption layer applied on top.
- **SDAT SHARC:** Secure archive format brought in with version 1.82 to prevent hacking and piracy.
- **CORE SHARC:** Format introduced with v1.82+. Used to secure local COREDATA sharc files in the client pkg.
- **Config SHARC:** These are used for online mode configuration files pushed to client during initial connection.

### Tool 2: Home Archive Unpacker

<div align="center">

![Home Archive Unpacker](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/0bc3877d-41cf-4fa9-be46-4386c3856344)

</div>

#### Usage
- Drag in one or more compatible archives or folders. It should be able to handle large tasks such as unpacking 60k objects in one operation.
- It will create a timestamp.txt in the output folder with the original timestamp of the archive, leave this in place and it will be used during future repacking of that same folder.
- Default output path for the archive Unpacker tool is next to the exe/Output/Mapped/. This can be changed in settings but will reset to default on next session.

## Tab 2: SceneID Generator / Decrypter

<div align="center">

![SceneID Generator / Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4204a35c-9dea-40d5-afed-5367d5e8fb75)

</div>

### Tool 1: Scene ID Generator

<div align="center">

![Scene ID Generator](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/fbc2f728-4c22-4f3d-9c2f-00091be53052)

</div>

#### Note
Scene IDs aka Channel IDs are very important to instancing in PlayStation Home. If 2 scenes share a Scene ID in SceneList.xml then players will be put into same instance.

#### Usage
- Enter a number between 1 and 65535, Click Encrypt.
- Also accepts hyphen separated ranges for bulk generation.

### Tool 2: Scene ID Decrypter

<div align="center">

![Scene ID Decrypter](https://github.com/DeViL303/MultiServer3-NuatilusFork/assets/24411577/4e5679fa-3fe0-4cf4-b393-651b15a7384c)

</div>

#### Note
If 2 scenes share a Scene ID in SceneList.xml then players will be put into same instance.

#### Usage
- Enter one or more Scene IDs (space, comma line separated).
- You can also *drag a plaintext SceneList.xml into the right hand side of the tab and it will parse it and decrypt all IDs.
  Don't drag the xml exactly onto the textbox.

