using System;
using System.Collections.Generic;
using System.IO;

namespace BetterJoyForCemu {
	public static class Config { // stores dynamic configuration, including
		static readonly string path;
		static Dictionary<string, string> variables = new Dictionary<string, string>();

		const int settingsNum = 11; // currently - ProgressiveScan, StartInTray + special buttons

        static Config() {
            path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\settings";
        }

		public static string GetDefaultValue(string s) {
			switch (s) {
				case "ProgressiveScan":
					return "1";
				case "capture":
					return "key_" + ((int)WindowsInput.Events.KeyCode.PrintScreen);
				case "reset_mouse":
					return "joy_" + ((int)Joycon.Button.STICK);
			}
			return "0";
		}

		// Helper function to count how many lines are in a file
		// https://www.dotnetperls.com/line-count
		static long CountLinesInFile(string f) {
			// Zero based count
			long count = -1;
			using (StreamReader r = new StreamReader(f)) {
				string line;
				while ((line = r.ReadLine()) != null) {
					count++;
				}
			}
			return count;
		}

		public static void Init(List<KeyValuePair<string, float[]>> caliIMUData, List<KeyValuePair<string, ushort[]>> caliSticksData) {
			foreach (string s in new string[] { "ProgressiveScan", "StartInTray", "capture", "home", "sl_l", "sl_r", "sr_l", "sr_r", "shake", "reset_mouse", "active_gyro" })
				variables[s] = GetDefaultValue(s);

			if (File.Exists(path)) {

				// Reset settings file if old settings
				if (CountLinesInFile(path) < settingsNum) {
					File.Delete(path);
					Init(caliIMUData, caliSticksData);
					return;
				}

				using (StreamReader file = new StreamReader(path)) {
					string line = String.Empty;
					int lineNO = 0;
					while ((line = file.ReadLine()) != null) {
						string[] vs = line.Split();
						try {
							if (lineNO < settingsNum) { // load in basic settings
								variables[vs[0]] = vs[1];
							} else { // load in calibration presets
                                if(lineNO == settingsNum) { // IMU
								    caliIMUData.Clear();
								    for (int i = 0; i < vs.Length; i++) {
									    string[] caliArr = vs[i].Split(',');
									    float[] newArr = new float[6];
									    for (int j = 1; j < caliArr.Length; j++) {
										    newArr[j - 1] = float.Parse(caliArr[j]);
									    }
									    caliIMUData.Add(new KeyValuePair<string, float[]>(
										    caliArr[0],
										    newArr
									    ));
								    }
                                } else if(lineNO == settingsNum + 1) { // Sticks
                                    caliSticksData.Clear();
								    for (int i = 0; i < vs.Length; i++) {
									    string[] caliArr = vs[i].Split(',');
									    ushort[] newArr = new ushort[12];
									    for (int j = 1; j < caliArr.Length; j++) {
										    newArr[j - 1] = ushort.Parse(caliArr[j]);
									    }
									    caliSticksData.Add(new KeyValuePair<string, ushort[]>(
										    caliArr[0],
										    newArr
									    ));
								    }
                                }
							}
						} catch { }
						lineNO++;
					}
				}
			} else {
				using (StreamWriter file = new StreamWriter(path)) {
					foreach (string k in variables.Keys)
						file.WriteLine(String.Format("{0} {1}", k, variables[k]));
					
                    // IMU Calibration
                    string caliStr = "";
					for (int i = 0; i < caliIMUData.Count; i++) {
						string space = " ";
						if (i == 0) space = "";
						caliStr += space + caliIMUData[i].Key + "," + String.Join(",", caliIMUData[i].Value);
					}
					file.WriteLine(caliStr);

                    // Stick Calibration
                    caliStr = "";
					for (int i = 0; i < caliSticksData.Count; i++) {
						string space = " ";
						if (i == 0) space = "";
						caliStr += space + caliSticksData[i].Key + "," + String.Join(",", caliSticksData[i].Value);
					}
					file.WriteLine(caliStr);
				}
			}
		}

		public static int IntValue(string key) {
			if (!variables.ContainsKey(key)) {
				return 0;
			}
			return Int32.Parse(variables[key]);
		}

		public static string Value(string key) {
			if (!variables.ContainsKey(key)) {
				return "";
			}
			return variables[key];
		}

		public static bool SetValue(string key, string value) {
			if (!variables.ContainsKey(key))
				return false;
			variables[key] = value;
			return true;
		}

		public static void SaveCaliIMUData(List<KeyValuePair<string, float[]>> caliData) {
			string[] txt = File.ReadAllLines(path);
			if (txt.Length < settingsNum + 1) // no custom IMU calibrations yet
				Array.Resize(ref txt, txt.Length + 1);

			string caliStr = "";
			for (int i = 0; i < caliData.Count; i++) {
				string space = " ";
				if (i == 0) space = "";
				caliStr += space + caliData[i].Key + "," + String.Join(",", caliData[i].Value);
			}
            txt[settingsNum] = caliStr;
            File.WriteAllLines(path, txt);
		}
        public static void SaveCaliSticksData(List<KeyValuePair<string, ushort[]>> caliData) {
			string[] txt = File.ReadAllLines(path);
			if (txt.Length < settingsNum + 2) // no custom sticks calibrations yet
				Array.Resize(ref txt, txt.Length + 1);

			string caliStr = "";
			for (int i = 0; i < caliData.Count; i++) {
				string space = " ";
				if (i == 0) space = "";
				caliStr += space + caliData[i].Key + "," + String.Join(",", caliData[i].Value);
			}
            txt[settingsNum + 1] = caliStr;
            File.WriteAllLines(path, txt);
		}

		public static void Save() {
			string[] txt = File.ReadAllLines(path);
			int NO = 0;
			foreach (string k in variables.Keys) {
				txt[NO] = String.Format("{0} {1}", k, variables[k]);
				NO++;
			}
			File.WriteAllLines(path, txt);
		}
	}
}
