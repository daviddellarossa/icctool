using ImageMagick;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace icctool
{
    class Program
    {
        static void Main(string[] args)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            
            try
            {
                if (!CheckInputParametersLength(args))
                {
                    return;
                }

                DirectoryInfo sourceDirectory = new DirectoryInfo(args[0]);
                if (!CheckSourceDirectoryExists(sourceDirectory))
                {
                    return;
                }

                FileInfo profileFileInfo = new FileInfo(args[1]);
                if (!CheckIccFileExists(profileFileInfo))
                {
                    return;
                }

                if (!CheckIccFileExtension(profileFileInfo))
                {
                    return;
                }

                var newColorProfile = new ImageMagick.ColorProfile(profileFileInfo.FullName);

                var filesToProcess = GetTiffFilesToProcess(sourceDirectory);

                Console.WriteLine($"{filesToProcess.Count()} files to process");


                var taskList = new List<Task>();
                foreach (var fileInfo in filesToProcess)
                {
                    var task = Task.Factory.StartNew(ProcessImage, (fileInfo, newColorProfile));
                    taskList.Add(task);
                }

                Task.WaitAll(taskList.ToArray());
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
            }

            stopWatch.Stop();

            Console.WriteLine($"Execution terminated in {stopWatch.Elapsed.Minutes} minutes and {stopWatch.Elapsed.Seconds} seconds");
        }

        private static async Task ProcessImage(object stateObject)
        {
            (FileInfo sourceFile, ImageMagick.ColorProfile colorProfile) = (ValueTuple<FileInfo, ImageMagick.ColorProfile>)stateObject;
            try
            {
                Console.WriteLine($"Processing file {sourceFile.FullName}");

                using (var image = new ImageMagick.MagickImage(sourceFile))
                {
                    ApplyColorProfileToImage(image, colorProfile);

                    ExportNewImage(image, sourceFile);

                    ExtractExifMetadataAsJsonFile(image, sourceFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {sourceFile}");
            }
        }

        private static void ExportNewImage(MagickImage image, FileInfo fileInfo)
        {
            var destinationFileFullPath = GetDestinationFileFullPath(fileInfo);

            image.Write(destinationFileFullPath);

            Console.WriteLine($"New File Created: {destinationFileFullPath}");
        }

        private static void ExtractExifMetadataAsJsonFile(MagickImage image, FileInfo fileInfo)
        {
            var exifDictionary = ExtractExifMetadata(image);

            string exifDictionaryJson = System.Text.Json.JsonSerializer.Serialize(exifDictionary);

            var jsonMetadataFileFullPath = GetJsonMetadataFileFullPath( fileInfo);

            Console.WriteLine($"Writing Exif metadata file {jsonMetadataFileFullPath}");

            File.WriteAllText(jsonMetadataFileFullPath, exifDictionaryJson);
        }

        private static string GetJsonMetadataFileFullPath(FileInfo sourceFile)
        {
            return Path.Combine(sourceFile.Directory.FullName, $"{System.IO.Path.GetFileNameWithoutExtension(sourceFile.Name)}.exif.json");
        }

        private static IDictionary<string, string> ExtractExifMetadata(MagickImage image)
        {
            Dictionary<string, string> exifMetadataDictionary = new Dictionary<string, string>();
            foreach(var attributeName in image.AttributeNames)
            {
                var attributeValue = image.GetAttribute(attributeName);
                exifMetadataDictionary.Add(attributeName, attributeValue);

            }
            return exifMetadataDictionary;
        }

        private static string GetDestinationFileFullPath(FileInfo sourceFile)
        {
            return Path.Combine(sourceFile.Directory.FullName, $"{System.IO.Path.GetFileNameWithoutExtension(sourceFile.Name)}_icc{sourceFile.Extension}");
        }

        private static void ApplyColorProfileToImage(MagickImage image, ColorProfile colorProfile)
        {
            if (image.HasProfile("icc"))
            {
                var isDone = image.TransformColorSpace(colorProfile, ColorTransformMode.HighRes);

                if (!isDone)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Something unexpected happend while transforming the colour space.");
                    Console.ResetColor();
                    return;
                }
            }
            else
            {
                image.SetProfile(colorProfile);
            }
        }

        private static bool CheckInputParametersLength(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine($"Wrong number of parameters. Expected 2, received {args.Length}");
                Console.WriteLine("Please specify the source folder containing the TIFF files and the icc file location");
                Console.WriteLine("Example: icctool \"C:\\pictures\" \"C:\\Documents\\icc\\profile.icc\"");
                return false;
            }
            return true;
        }

        private static bool CheckSourceDirectoryExists(DirectoryInfo sourceDirectory)
        {
            if (!sourceDirectory.Exists)
            {
                Console.WriteLine($"Directory '{sourceDirectory.FullName}' does not exist");
                return false;
            }
            return true;
        }

        private static bool CheckIccFileExists(FileInfo iccFile)
        {
            if (!iccFile.Exists)
            {
                Console.WriteLine($"File '{iccFile.FullName}' does not exist");
                return false;
            }
            return true;
        }

        private static bool CheckIccFileExtension(FileInfo iccFile)
        {
            if (!iccFile.Extension.Equals(".icc", StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine($"Second parameter must be an '.icc' file");
                return false;
            }
            return true;
        }

        private static IEnumerable<FileInfo> GetTiffFilesToProcess(DirectoryInfo sourceDirectory)
        {
            return sourceDirectory.EnumerateFiles("*.tif")
                .Where(x => !x.Name.EndsWith("_icc.tif", StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
