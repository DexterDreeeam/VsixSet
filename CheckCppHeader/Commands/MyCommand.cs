using Microsoft.VisualStudio;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CheckCppHeader
{
    public class ChhConfig
    {
        public List<string> TraverseFolders;

        public List<string> ScreeningCppPathKeywords;

        public List<string> ScreeningIncludeHeaderKeywords;

        public List<string> RemoveFolders;

        public string CompileProject;

        public List<string> SuccessFlagFiles;
    }


    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        public const string config_json_path = "C:/CheckCppHeader/cch-config.json";

        public ChhConfig cch_config;

        public Project compile_project;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                string cch_config_str = "";
                cch_config_str = File.ReadAllText(config_json_path);
                Output($"Config: '{cch_config_str}'.");
                cch_config = JsonConvert.DeserializeObject<ChhConfig>(cch_config_str);
            }
            catch (Exception ex)
            {
                Output($"Exception: {ex.Message}.");
            }

            var projs = await VS.Solutions.GetAllProjectsAsync().ConfigureAwait(false);

            foreach (var proj in projs)
            {
                if (proj.Name.Contains(cch_config.CompileProject))
                {
                    compile_project = proj;
                    break;
                }
            }

            foreach (var folder in cch_config.TraverseFolders)
            {
                var files = Directory.GetFiles(folder, "*.cpp", SearchOption.AllDirectories);

                foreach (var f in files)
                {
                    bool is_target_file = true;
                    foreach (var screen_word in cch_config.ScreeningCppPathKeywords)
                    {
                        if (f.Contains(screen_word))
                        {
                            is_target_file = false;
                            break;
                        }
                    }
                    if (is_target_file)
                    {
                        await ProcessCppFileAsync(f).ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task ProcessCppFileAsync(string path)
        {
            Output($"Inspecting file: '{path}'...");

            string backup_file_path = path + ".backup";
            try
            {
                File.Copy(path, backup_file_path, true);
            }
            catch (Exception ex)
            {
                Output($"Exception: {ex.Message}.");
            }

            var lines = File.ReadAllLines(backup_file_path);
            int line_no = 0;

            while (line_no < lines.Length)
            {
                if (!lines[line_no].Contains("#include"))
                {
                    ++line_no;
                    continue;
                }

                bool has_screen_word = false;
                foreach (var screen_word in cch_config.ScreeningIncludeHeaderKeywords)
                {
                    if (lines[line_no].Contains(screen_word))
                    {
                        has_screen_word = true;
                        break;
                    }
                }
                if (has_screen_word)
                {
                    ++line_no;
                    continue;
                }

                var origin_line = lines[line_no];
                lines[line_no] = "";
                File.WriteAllLines(path, lines);

                Output($"Try to file: '{path}' line '{origin_line}' and compile...");

                bool compile_rst = await TryCompileAsync().ConfigureAwait(false);
                if (compile_rst)
                {
                    Output($"File: '{path}' line '{origin_line}' is removed.");

                    var lines_list = lines.ToList();
                    lines_list.RemoveAt(line_no);
                    lines = lines_list.ToArray();
                    File.WriteAllLines(path, lines);
                }
                else
                {
                    lines[line_no] = origin_line;
                    File.WriteAllLines(path, lines);
                    ++line_no;
                }
            }

            try
            {
                File.Delete(backup_file_path);
            }
            catch (Exception ex)
            {
                Output($"Exception: {ex.Message}.");
            }
        }

        public async Task<bool> TryCompileAsync()
        {
            foreach (var f in cch_config.RemoveFolders)
            {
                try
                {
                    Directory.Delete(f, true);
                }
                catch (Exception ex)
                {
                    Output($"Exception: {ex.Message}.");
                }
            }

            await compile_project.BuildAsync(BuildAction.Rebuild).ConfigureAwait(false);

            Build b;
            foreach (var test in cch_config.SuccessFlagFiles)
            {
                if (!File.Exists(test))
                {
                    return false;
                }
            }

            return true;
        }

        public void Output(string str)
        {
            Package.GetOutputPane(VSConstants.GUID_OutWindowGeneralPane, "Cch").OutputString($"{str}\n");
        }
    }
}
