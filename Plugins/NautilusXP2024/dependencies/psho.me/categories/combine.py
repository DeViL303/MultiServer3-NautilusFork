import os

def extract_lines_from_txt(file_path):
    """Extract lines from the specified text file."""
    lines = []
    with open(file_path, 'r', encoding='utf-8') as file:
        for line in file:
            lines.append(line.strip())
    return lines

def combine_unique_lines(lines_list):
    """Combine lines from multiple lists and remove duplicates."""
    unique_lines = set()
    for lines in lines_list:
        unique_lines.update(lines)
    return sorted(unique_lines)

def write_lines_to_txt(lines, output_file):
    """Write the unique lines to an output text file."""
    with open(output_file, 'w', encoding='utf-8') as file:
        for line in lines:
            file.write(line + '\n')

def scan_and_combine_txt_files(directory, output_file):
    """Scan text files in the directory, combine unique lines, and write to a new text file."""
    all_lines = []
    for filename in os.listdir(directory):
        if filename.endswith(".xml"):  # Assuming the files have .xml extension but are actually text files
            file_path = os.path.join(directory, filename)
            lines = extract_lines_from_txt(file_path)
            if lines:  # Only add non-empty lists
                all_lines.append(lines)
    
    unique_lines = combine_unique_lines(all_lines)
    write_lines_to_txt(unique_lines, output_file)

if __name__ == "__main__":
    try:
        # Get the directory where the script is located
        script_directory = os.path.dirname(os.path.abspath(__file__))
        output_file = os.path.join(script_directory, 'All_Items.xml')

        scan_and_combine_txt_files(script_directory, output_file)
        print("Processing completed successfully.")
    except Exception as e:
        print(f"An error occurred: {e}")
    finally:
        input("Press Enter to exit...")
