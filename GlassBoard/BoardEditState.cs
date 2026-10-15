using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GlassBoard;

[DataContract]
public class BoardEditState
{
	[DataMember]
	public List<Entry> Apps;

	[DataMember]
	public List<string> Groups;

	[DataMember]
	public List<string> Removed;

	[DataMember]
	public Dictionary<string, string> Learned;

	public static BoardEditState Capture(Preferences p)
	{
		return new BoardEditState
		{
			Apps = p.Apps.Select(FeatureRules.Copy).ToList(),
			Groups = p.Groups.ToList(),
			Removed = p.Removed.ToList(),
			Learned = new Dictionary<string, string>(p.LearnedGroups)
		};
	}

	public void Apply(Preferences p)
	{
		p.Apps = Apps.Select(FeatureRules.Copy).ToList();
		p.Groups = Groups.ToList();
		p.Removed = Removed.ToList();
		p.LearnedGroups = new Dictionary<string, string>(Learned);
		p.Normalize();
	}

	public string Signature()
	{
		using MemoryStream memoryStream = new MemoryStream();
		new DataContractJsonSerializer(typeof(BoardEditState)).WriteObject((Stream)memoryStream, (object)this);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}
}
