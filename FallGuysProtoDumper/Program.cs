// This is an example showing how to use the .NET type model in Il2CppInspector
// to re-construct .proto files for applications using protobuf-net

// Copyright 2020 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
// https://github.com/djkaty/Il2CppProtoExtractor-FallGuys
// https://github.com/djkaty/Il2CppInspector
// http://www.djkaty.com/tag/il2cpp

// This example uses "Fall Guys: Ultimate Knockout" as the target:
// Steam package: https://steamdb.info/sub/369927/
// Game version: 2020-08-04
// GameAssembly.dll - CRC32: F448429A
// global-metadata.dat - CRC32: 98DFE664

// Il2CppInspector: https://github.com/djkaty/Il2CppInspector
// protobuf-net: https://github.com/protobuf-net/protobuf-net

// References: 
// https://developers.google.com/protocol-buffers/docs/overview
// https://code.google.com/archive/p/protobuf-net/wikis/GettingStarted.wiki
// http://loyc.net/2013/protobuf-net-unofficial-manual.html#:~:text=Protobuf%2Dnet%20can%20serialize%20a,feature%20is%20disabled%20by%20default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInspector.Reflection;

namespace FallGuysProtoDumper
{
    class Program
    {
        // Set the path to your metadata and binary files here
        public static string MetadataFile = @"F:\Source\Repos\Il2CppInspector\Il2CppTests\TestBinaries\FallGuys\global-metadata.dat";
        public static string BinaryFile = @"F:\Source\Repos\Il2CppInspector\Il2CppTests\TestBinaries\FallGuys\GameAssembly.dll";

        // Set the path to your desired output here
        public static string ProtoFile = @"fallguys.proto";

        private static StringBuilder proto = new StringBuilder();

        // Define type map from .NET types to protobuf types
        // This is specifically how protobuf-net maps types and is not the same for all .NET protobuf libraries
        private static Dictionary<string, string> protoTypes = new Dictionary<string, string> {
            ["System.Int32"] = "int32",
            ["System.UInt32"] = "uint32",
            ["System.Byte"] = "uint32",
            ["System.SByte"] = "int32",
            ["System.UInt16"] = "uint32",
            ["System.Int16"] = "int32",
            ["System.Int64"] = "int64",
            ["System.UInt64"] = "uint64",
            ["System.Single"] = "float",
            ["System.Double"] = "double",
            ["System.Decimal"] = "bcl.Decimal",
            ["System.Boolean"] = "bool",
            ["System.String"] = "string",
            ["System.Byte[]"] = "bytes",
            ["System.Char"] = "uint32",
            ["System.DateTime"] = "google.protobuf.Timestamp"
        };

        private static Dictionary<ulong, byte> vaFieldMapping;

        static void Main(string[] args) {

            // First we load the binary and metadata files into Il2CppInspector
            // There is only one image so we use [0] to select this image
            Console.WriteLine("Loading package...");
            var package = Il2CppInspector.Il2CppInspector.LoadFromFile(BinaryFile, MetadataFile, silent: true)[0];

            // Now we create the .NET type model from the package
            // This creates a .NET Reflection-style interface we can query with Linq
            Console.WriteLine("Creating type model...");
            var model = new TypeModel(package);

            // All protobuf messages have this class attribute
            var protoContract = model.GetType("ProtoBuf.ProtoContractAttribute");

            // Get all the messages by searching for types with [ProtoContract]
            var messages = model.TypesByDefinitionIndex.Where(t => t.CustomAttributes.Any(a => a.AttributeType == protoContract));

            // All protobuf fields have this property attribute
            var protoMember = model.GetType("ProtoBuf.ProtoMemberAttribute");

            // Get all of the custom attributes generators for ProtoMember so we can determine field numbers
            var atts = model.CustomAttributeGenerators[protoMember];

            // Create a mapping of CAG virtual addresses to field numbers by reading the disassembly code
            vaFieldMapping = atts.Select(a => new {
                VirtualAddress = a.VirtualAddress.Start,
                FieldNumber    = a.GetMethodBody()[0x0D]
            })
            .ToDictionary(kv => kv.VirtualAddress, kv => kv.FieldNumber);

            // Find CAGs which are used by other attribute types and shared with ProtoMember
            var sharedAtts = vaFieldMapping.Keys.Select(a => model.CustomAttributeGeneratorsByAddress[a].Where(a => a.AttributeType != protoMember))
                .SelectMany(l => l);

            // Warn about shared mappings
            foreach (var item in sharedAtts)
                Console.WriteLine($"WARNING: Attribute generator {item.VirtualAddress.ToAddressString()} is shared with {item.AttributeType.FullName} - check disassembly listing");

            // Fixups we have determined from the disassembly
            vaFieldMapping[0x180055270] = 5;
            vaFieldMapping[0x180005660] = 7;
            vaFieldMapping[0x18002FCB0] = 1;

            // Keep a list of all the enums we need to output (HashSet ensures unique values - we only want each enum once!)
            var enums = new HashSet<TypeInfo>();

            // Let's iterate over all of the messages and find all of the fields
            // This is any field or property with the [ProtoMember] attribute
            foreach (var message in messages) {
                var name   = message.CSharpName;
                var fields = message.DeclaredFields.Where(f => f.CustomAttributes.Any(a => a.AttributeType == protoMember));
                var props  = message.DeclaredProperties.Where(p => p.CustomAttributes.Any(a => a.AttributeType == protoMember));

                proto.Append($"message {name} {{\n");

                // Output C# fields
                foreach (var field in fields) {
                    var pmAtt = field.CustomAttributes.First(a => a.AttributeType == protoMember);
                    outputField(field.Name, field.FieldType, pmAtt);

                    if (field.FieldType.IsEnum)
                        enums.Add(field.FieldType);
                }

                // Output C# properties
                foreach (var prop in props) {
                    var pmAtt = prop.CustomAttributes.First(a => a.AttributeType == protoMember);
                    outputField(prop.Name, prop.PropertyType, pmAtt);

                    if (prop.PropertyType.IsEnum)
                        enums.Add(prop.PropertyType);
                }

                proto.Append("}\n\n");
            }

            // Output enums
            var enumText = new StringBuilder();

            foreach (var e in enums) {
                enumText.Append("enum " + e.Name + " {\n");
                var namesAndValues = e.GetEnumNames().Zip(e.GetEnumValues().Cast<int>(), (n, v) => n + " = " + v);
                foreach (var nv in namesAndValues)
                    enumText.Append("  " + nv + ";\n");
                enumText.Append("}\n\n");
            }

            // Output messages
            var banner = @"// Proto file reconstruction tutorial example
// For educational purposes only
// https://github.com/djkaty/Il2CppProtoExtractor-FallGuys
// https://github.com/djkaty/Il2CppInspector
// http://www.djkaty.com/tag/il2cpp

syntax=""proto3"";
";

            File.WriteAllText(ProtoFile, banner + enumText.ToString() + proto.ToString());
        }

        private static void outputField(string name, TypeInfo type, CustomAttributeData pmAtt) {
            // Handle arrays
            var isRepeated = type.IsArray;
            var isOptional = false;

            var typeFullName = isRepeated? type.ElementType.FullName : type.FullName ?? string.Empty;
            var typeFriendlyName = isRepeated? type.ElementType.Name : type.Name;

            // Handle one-dimensional collections like lists
            // We could also use type.Namespace == "System.Collections.Generic" && type.UnmangledBaseName == "List"
            // or typeBaseName == "System.Collections.Generic.List`1" but these are less flexible
            if (type.ImplementedInterfaces.Any(i => i.FullName == "System.Collections.Generic.IList`1")) {
                // Get the type of the IList by looking at its first generic argument
                // Note this is a naive implementation which doesn't handle nesting of lists or arrays in lists etc.

                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = type.GenericTypeArguments[0].Name;
                isRepeated = true;
            }

            // Handle maps (IDictionary)
            if (type.ImplementedInterfaces.Any(i => i.FullName == "System.Collections.Generic.IDictionary`2")) {

                // This time we have two generic arguments to deal with - the key and the value
                var keyFullName = type.GenericTypeArguments[0].FullName;
                var valueFullName = type.GenericTypeArguments[1].FullName;

                // We're going to have to deal with building this proto type name separately from the value types below
                // We don't set isRepeated because it's implied by using a map type
                protoTypes.TryGetValue(keyFullName, out var keyFriendlyName);
                protoTypes.TryGetValue(valueFullName, out var valueFriendlyName);
                typeFriendlyName = $"map<{keyFriendlyName ?? type.GenericTypeArguments[0].Name}, {valueFriendlyName ?? type.GenericTypeArguments[1].Name}>";
            }

            // Handle nullable types
            if (type.FullName == "System.Nullable`1") {
                // Once again we look at the first generic argument to get the real type

                typeFullName = type.GenericTypeArguments[0].FullName;
                typeFriendlyName = type.GenericTypeArguments[0].Name;
                isOptional = true;
            }

            // Handle primitive value types
            if (protoTypes.TryGetValue(typeFullName, out var protoTypeName))
                typeFriendlyName = protoTypeName;

            // Handle repeated fields
            var annotatedName = typeFriendlyName;
            if (isRepeated)
                annotatedName = "repeated " + annotatedName;

            // Handle nullable (optional) fields
            if (isOptional)
                annotatedName = "optional " + annotatedName;

            // Output field
            proto.Append($"  {annotatedName} {name} = {vaFieldMapping[pmAtt.VirtualAddress.Start]};\n");
        }
    }
}
