namespace indian_ticketing;

public static class StationData
{
    public static readonly (string Name, string Code)[] Stations =
    {
        // Delhi / NCR
        ("New Delhi", "NDLS"),
        ("Old Delhi Junction", "DLI"),
        ("Hazrat Nizamuddin", "NZM"),
        ("Anand Vihar Terminal", "ANVT"),
        ("Delhi Sarai Rohilla", "DEE"),
        ("Ghaziabad Junction", "GZB"),
        ("Faridabad", "FDB"),
        ("Gurgaon", "GGN"),
        ("Palwal", "PWL"),

        // Uttar Pradesh
        ("Mathura Junction", "MTJ"),
        ("Agra Cantt", "AGC"),
        ("Agra Fort", "AF"),
        ("Aligarh Junction", "ALJN"),
        ("Tundla Junction", "TDL"),
        ("Firozabad", "FZD"),
        ("Etawah", "ETW"),
        ("Kanpur Central", "CNB"),
        ("Lucknow Junction", "LKO"),
        ("Lucknow NER", "LJN"),
        ("Varanasi Junction", "BSB"),
        ("Prayagraj Junction", "PRYJ"),
        ("Allahabad Junction", "ALD"),
        ("Jhansi Junction", "JHS"),
        ("Gwalior Junction", "GWL"),
        ("Moradabad Junction", "MB"),
        ("Bareilly Junction", "BE"),
        ("Saharanpur Junction", "SRE"),
        ("Meerut City", "MTC"),
        ("Hapur", "HPU"),
        ("Gorakhpur Junction", "GKP"),
        ("Gonda Junction", "GD"),
        ("Faizabad Junction", "FD"),
        ("Basti Junction", "BST"),
        ("Orai", "ORAI"),

        // Rajasthan
        ("Jaipur Junction", "JP"),
        ("Ajmer Junction", "AII"),
        ("Udaipur City", "UDZ"),
        ("Jodhpur Junction", "JU"),
        ("Bikaner Junction", "BKN"),
        ("Jaisalmer", "JSM"),
        ("Kota Junction", "KOTA"),
        ("Alwar Junction", "AWR"),
        ("Bharatpur Junction", "BTE"),
        ("Sawai Madhopur Junction", "SWM"),
        ("Chittaurgarh Junction", "COR"),
        ("Sikar Junction", "SIKR"),
        ("Gangapur City Junction", "GAC"),

        // Madhya Pradesh
        ("Bhopal Junction", "BPL"),
        ("Habibganj", "HBJ"),
        ("Indore Junction BG", "INDB"),
        ("Ujjain Junction", "UJN"),
        ("Ratlam Junction", "RTM"),
        ("Jabalpur Junction", "JBP"),
        ("Katni Junction", "KTE"),
        ("Satna Junction", "STA"),
        ("Rewa", "REWA"),
        ("Itarsi Junction", "ET"),
        ("Khandwa Junction", "KNW"),
        ("Guna Junction", "GUNA"),
        ("Sagar", "SGO"),
        ("Vidisha", "BHS"),
        ("Khajuraho", "KHJR"),

        // Gujarat
        ("Ahmedabad Junction", "ADI"),
        ("Vadodara Junction", "BRC"),
        ("Surat", "ST"),
        ("Rajkot Junction", "RJT"),
        ("Bhavnagar Terminus", "BVP"),
        ("Anand Junction", "ANND"),
        ("Gandhinagar Capital", "GNC"),
        ("Porbandar", "PBR"),
        ("Jamnagar", "JAM"),
        ("Veraval", "VRL"),
        ("Palanpur Junction", "PNU"),
        ("Mehsana Junction", "MSH"),
        ("Nadiad Junction", "ND"),

        // Maharashtra
        ("Mumbai Central", "BCT"),
        ("Chhatrapati Shivaji Maharaj T", "CSMT"),
        ("Lokmanya Tilak Terminus", "LTT"),
        ("Bandra Terminus", "BDTS"),
        ("Dadar", "DR"),
        ("Borivali", "BVI"),
        ("Thane", "TNA"),
        ("Kalyan Junction", "KYN"),
        ("Panvel", "PNVL"),
        ("Vasai Road", "BSR"),
        ("Pune Junction", "PUNE"),
        ("Nashik Road", "NK"),
        ("Igatpuri", "IGP"),
        ("Aurangabad", "AWB"),
        ("Nanded", "NED"),
        ("Solapur Junction", "SUR"),
        ("Kolhapur", "KOP"),
        ("Miraj Junction", "MRJ"),
        ("Nagpur Junction", "NGP"),
        ("Bhusawal Junction", "BSL"),
        ("Akola Junction", "AK"),
        ("Amravati", "AMI"),
        ("Wardha Junction", "WR"),

        // Haryana / Punjab
        ("Ambala Cantt Junction", "UMB"),
        ("Ambala City", "UBC"),
        ("Chandigarh Junction", "CDG"),
        ("Ludhiana Junction", "LDH"),
        ("Amritsar Junction", "ASR"),
        ("Jalandhar City", "JUC"),
        ("Jalandhar Cantt Junction", "JRC"),
        ("Pathankot Cantt", "PTKC"),
        ("Ferozepur Cantt", "FZR"),
        ("Bhatinda Junction", "BTI"),
        ("Patiala", "PTA"),

        // Uttarakhand / Himachal
        ("Dehradun", "DDN"),
        ("Haridwar Junction", "HW"),
        ("Roorkee", "RK"),
        ("Rishikesh", "RKSH"),
        ("Kalka", "KLK"),
        ("Shimla", "SML"),

        // Jammu & Kashmir
        ("Jammu Tawi", "JAT"),
        ("Udhampur", "UHP"),
        ("Katra", "SVDK"),

        // Bihar
        ("Patna Junction", "PNBE"),
        ("Gaya Junction", "GAYA"),
        ("Muzaffarpur Junction", "MFP"),
        ("Danapur", "DNR"),
        ("Bhagalpur Junction", "BGP"),
        ("Katihar Junction", "KIR"),
        ("Darbhanga Junction", "DBG"),
        ("Samastipur Junction", "SPJ"),
        ("Hajipur Junction", "HJP"),
        ("Sitamarhi", "SMI"),

        // Jharkhand
        ("Ranchi Junction", "RNC"),
        ("Dhanbad Junction", "DHN"),
        ("Bokaro Steel City", "BKSC"),
        ("Tatanagar Junction", "TATA"),
        ("Chakradharpur Junction", "CKP"),

        // West Bengal
        ("Howrah Junction", "HWH"),
        ("Sealdah", "SDAH"),
        ("Kolkata", "KOAA"),
        ("Shalimar", "SHM"),
        ("Kharagpur Junction", "KGP"),
        ("Asansol Junction", "ASN"),
        ("Durgapur", "DGR"),
        ("Burdwan Junction", "BWN"),
        ("New Jalpaiguri", "NJP"),
        ("Malda Town", "MLDT"),

        // Assam / Northeast
        ("Guwahati", "GHY"),
        ("Dibrugarh Town", "DBRG"),
        ("Lumding Junction", "LMG"),
        ("New Bongaigaon Junction", "NBQ"),
        ("Agartala", "AGTL"),
        ("Kishanganj", "KNE"),
        ("New Tinsukia Junction", "NTSK"),

        // Odisha
        ("Bhubaneswar", "BBS"),
        ("Cuttack Junction", "CTC"),
        ("Puri", "PURI"),
        ("Khurda Road Junction", "KUR"),
        ("Brahmapur", "BAM"),
        ("Rourkela Junction", "ROU"),
        ("Sambalpur Junction", "SBP"),
        ("Vizianagaram Junction", "VZM"),

        // Chhattisgarh
        ("Raipur Junction", "R"),
        ("Bilaspur Junction", "BSP"),
        ("Durg Junction", "DURG"),

        // Telangana
        ("Secunderabad Junction", "SC"),
        ("Hyderabad Deccan", "HYB"),
        ("Kacheguda", "KCG"),
        ("Warangal", "WL"),
        ("Kazipet Junction", "KZJ"),
        ("Nalgonda", "NGD"),

        // Andhra Pradesh
        ("Visakhapatnam Junction", "VSKP"),
        ("Vijayawada Junction", "BZA"),
        ("Guntur Junction", "GNT"),
        ("Rajahmundry", "RJY"),
        ("Nellore", "NLR"),
        ("Tirupati", "TPTY"),
        ("Renigunta Junction", "RU"),
        ("Guntakal Junction", "GTL"),
        ("Kakinada Town", "COA"),
        ("Tenali Junction", "TEL"),

        // Karnataka
        ("KSR Bengaluru City Junction", "SBC"),
        ("Yesvantpur Junction", "YPR"),
        ("Bengaluru Cantonment", "BNC"),
        ("Mysuru Junction", "MYS"),
        ("Hubli Junction", "UBL"),
        ("Dharwad", "DWR"),
        ("Gadag Junction", "GDG"),
        ("Mangaluru Central", "MAQ"),
        ("Mangaluru Junction", "MAJN"),
        ("Londa Junction", "LD"),
        ("Hassan", "HAS"),
        ("Tumkur", "TK"),
        ("Davangere", "DVG"),
        ("Ballari Junction", "BAY"),
        ("Gulbarga Junction", "GR"),

        // Goa
        ("Madgaon Junction", "MAO"),
        ("Vasco Da Gama", "VSG"),
        ("Karmali", "KRMI"),
        ("Thivim", "THVM"),

        // Tamil Nadu
        ("Chennai Central", "MAS"),
        ("Chennai Egmore", "MS"),
        ("Tambaram", "TBM"),
        ("Chengalpattu Junction", "CGL"),
        ("Villupuram Junction", "VM"),
        ("Pondicherry", "PDY"),
        ("Vellore Cantt", "VN"),
        ("Katpadi Junction", "KPD"),
        ("Coimbatore Junction", "CBE"),
        ("Erode Junction", "ED"),
        ("Salem Junction", "SA"),
        ("Tiruchirapalli Junction", "TPJ"),
        ("Madurai Junction", "MDU"),
        ("Tirunelveli Junction", "TEN"),
        ("Nagercoil Junction", "NCJ"),
        ("Rameswaram", "RMM"),
        ("Tuticorin", "TUT"),
        ("Tirupur", "TUP"),
        ("Dindigul Junction", "DG"),
        ("Thanjavur Junction", "TJ"),
        ("Kumbakonam", "KMU"),
        ("Mayiladuthurai Junction", "MV"),

        // Kerala
        ("Thiruvananthapuram Central", "TVC"),
        ("Kollam Junction", "QLN"),
        ("Kayamkulam Junction", "KYJ"),
        ("Alappuzha", "ALLP"),
        ("Ernakulam Junction", "ERS"),
        ("Ernakulam Town", "ERN"),
        ("Thrissur", "TCR"),
        ("Shoranur Junction", "SRR"),
        ("Palakkad Junction", "PGT"),
        ("Kozhikode", "CLT"),
        ("Kannur Junction", "CAN"),
        ("Kottayam", "KTYM"),
    };

    public static string ToSlug(string name, string code)
    {
        var slug = name.ToLowerInvariant()
                       .Replace(' ', '-')
                       .Replace("'", "")
                       .Replace(".", "")
                       .Replace("(", "")
                       .Replace(")", "");
        return slug + "-" + code;
    }

    public static IEnumerable<(string Name, string Code)> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        var q = query.Trim().ToUpperInvariant();
        return Stations
            .Where(s => s.Code.StartsWith(q, StringComparison.Ordinal) ||
                        s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Code.StartsWith(q, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(s => s.Name)
            .Take(10);
    }
}
