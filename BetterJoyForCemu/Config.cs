using System;
using System.Collections.Generic;
using System.IO;
using WindowsInput.Events;

namespace BetterJoyForCemu
{
    public static class Config
    {
        private const int settingsNum = 11; // currently - ProgressiveScan, StartInTray + special buttons

        // stores dynamic configuration, including
        private static readonly string path;
        private static readonly Dictionary<string, string> variables = new();

        static Config()
        {
            path = Path.GetDirectoryName(Environment.ProcessPath) + "\\settings";
        }

        public static string GetDefaultValue(string s)
        {
            switch (s)
            {
                case "ProgressiveScan":
                    return "1";
                case "capture":
                    return "key_" + (int)KeyCode.PrintScreen;
                case "reset_mouse":
                    return "joy_" + (int)Joycon.Button.STICK;
            }

            return "0";
        }

        // Helper function to count how many lines are in a file
        // https://www.dotnetperls.com/line-count
        private static long CountLinesInFile(string f)
        {
            // Zero based count
            long count = -1;
            using (var r = new StreamReader(f))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    count++;
                }
            }

            return count;
        }

        public static void Init(
            List<KeyValuePair<string, float[]>> caliIMUData,
            List<KeyValuePair<string, ushort[]>> caliSticksData
        )
        {
            foreach (var s in new[]
                     {
                         "ProgressiveScan", "StartInTray", "capture", "home", "sl_l", "sl_r", "sr_l", "sr_r",
                         "shake", "reset_mouse", "active_gyro"
                     })
            {
                variables[s] = GetDefaultValue(s);
            }

            if (File.Exists(path))
            {
                // Reset settings file if old settings
                if (CountLinesInFile(path) < settingsNum)
                {
                    File.Delete(path);
                    Init(caliIMUData, caliSticksData);
                    return;
                }

                using (var file = new StreamReader(path))
                {
                    var line = string.Empty;
                    var lineNO = 0;
                    while ((line = file.ReadLine()) != null)
                    {
                        var vs = line.Split();
                        try
                        {
                            if (lineNO < settingsNum)
                            {
                                // load in basic settings
                                variables[vs[0]] = vs[1];
                            }
                            else
                            {
                                // load in calibration presets
                                if (lineNO == settingsNum)
                                {
                                    // IMU
                                    caliIMUData.Clear();
                                    for (var i = 0; i < vs.Length; i++)
                                    {
                                        var caliArr = vs[i].Split(',');
                                        var newArr = new float[6];
                                        for (var j = 1; j < caliArr.Length; j++)
                                        {
                                            newArr[j - 1] = float.Parse(caliArr[j]);
                                        }

                                        caliIMUData.Add(
                                            new KeyValuePair<string, float[]>(
                                                caliArr[0],
                                                newArr
                                            )
                                        );
                                    }
                                }
                                else if (lineNO == settingsNum + 1)
                                {
                                    // Sticks
                                    caliSticksData.Clear();
                                    for (var i = 0; i < vs.Length; i++)
                                    {
                                        var caliArr = vs[i].Split(',');
                                        var newArr = new ushort[12];
                                        for (var j = 1; j < caliArr.Length; j++)
                                        {
                                            newArr[j - 1] = ushort.Parse(caliArr[j]);
                                        }

                                        caliSticksData.Add(
                                            new KeyValuePair<string, ushort[]>(
                                                caliArr[0],
                                                newArr
                                            )
                                        );
                                    }
                                }
                            }
                        }
                        catch { }

                        lineNO++;
                    }
                }
            }
            else
            {
                using (var file = new StreamWriter(path))
                {
                    foreach (var k in variables.Keys)
                    {
                        file.WriteLine("{0} {1}", k, variables[k]);
                    }

                    // IMU Calibration
                    var caliStr = "";
                    for (var i = 0; i < caliIMUData.Count; i++)
                    {
                        var space = " ";
                        if (i == 0)
                        {
                            space = "";
                        }

                        caliStr += space + caliIMUData[i].Key + "," + string.Join(",", caliIMUData[i].Value);
                    }

                    file.WriteLine(caliStr);

                    // Stick Calibration
                    caliStr = "";
                    for (var i = 0; i < caliSticksData.Count; i++)
                    {
                        var space = " ";
                        if (i == 0)
                        {
                            space = "";
                        }

                        caliStr += space + caliSticksData[i].Key + "," + string.Join(",", caliSticksData[i].Value);
                    }

                    file.WriteLine(caliStr);
                }
            }
        }

        public static int IntValue(string key)
        {
            if (!variables.ContainsKey(key))
            {
                return 0;
            }

            return int.Parse(variables[key]);
        }

        public static string Value(string key)
        {
            if (!variables.ContainsKey(key))
            {
                return "";
            }

            return variables[key];
        }

        public static bool SetValue(string key, string value)
        {
            if (!variables.ContainsKey(key))
            {
                return false;
            }

            variables[key] = value;
            return true;
        }

        public static void SaveCaliIMUData(List<KeyValuePair<string, float[]>> caliData)
        {
            var txt = File.ReadAllLines(path);
            if (txt.Length < settingsNum + 1) // no custom IMU calibrations yet
            {
                Array.Resize(ref txt, txt.Length + 1);
            }

            var caliStr = "";
            for (var i = 0; i < caliData.Count; i++)
            {
                var space = " ";
                if (i == 0)
                {
                    space = "";
                }

                caliStr += space + caliData[i].Key + "," + string.Join(",", caliData[i].Value);
            }

            txt[settingsNum] = caliStr;
            File.WriteAllLines(path, txt);
        }

        public static void SaveCaliSticksData(List<KeyValuePair<string, ushort[]>> caliData)
        {
            var txt = File.ReadAllLines(path);
            if (txt.Length < settingsNum + 2) // no custom sticks calibrations yet
            {
                Array.Resize(ref txt, txt.Length + 1);
            }

            var caliStr = "";
            for (var i = 0; i < caliData.Count; i++)
            {
                var space = " ";
                if (i == 0)
                {
                    space = "";
                }

                caliStr += space + caliData[i].Key + "," + string.Join(",", caliData[i].Value);
            }

            txt[settingsNum + 1] = caliStr;
            File.WriteAllLines(path, txt);
        }

        public static void Save()
        {
            var txt = File.ReadAllLines(path);
            var NO = 0;
            foreach (var k in variables.Keys)
            {
                txt[NO] = string.Format("{0} {1}", k, variables[k]);
                NO++;
            }

            File.WriteAllLines(path, txt);
        }
    }
}
