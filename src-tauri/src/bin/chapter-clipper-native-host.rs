use regex::Regex;
use serde::{Deserialize, Serialize};
use std::{
    fs,
    io::{self, Read, Write},
    path::{Path, PathBuf},
    process::{Command, Stdio},
};

#[cfg(windows)]
use std::os::windows::process::CommandExt;

#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x08000000;
const FALLBACK_YT_DLP: &str = r"C:\Users\paul\projects\YouTube\backend\yt-dlp.exe";
const YOUTUBE_DRIVE_DIR: &str = r"G:\My Drive\video-drives";

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Request {
    action: String,
    url: Option<String>,
    chapters: Option<Vec<Chapter>>,
    cookies: Option<Vec<Cookie>>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Chapter {
    index: usize,
    title: String,
    start_time: f64,
    end_time: f64,
    duration: f64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Cookie {
    domain: Option<String>,
    path: Option<String>,
    secure: Option<bool>,
    expiration_date: Option<f64>,
    name: String,
    value: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Clip {
    index: usize,
    title: String,
    file_path: String,
    duration: f64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Response {
    success: bool,
    error: Option<String>,
    output_dir: Option<String>,
    clips: Vec<Clip>,
    ready: Option<bool>,
}

fn find_executable(names: &[&str]) -> Option<PathBuf> {
    for name in names {
        let mut command = Command::new("where.exe");
        command.arg(name);
        #[cfg(windows)]
        command.creation_flags(CREATE_NO_WINDOW);
        let output = command.output().ok()?;
        if output.status.success() {
            if let Some(path) = String::from_utf8_lossy(&output.stdout).lines().next() {
                return Some(PathBuf::from(path.trim()));
            }
        }
    }
    None
}

fn yt_dlp() -> Result<PathBuf, String> {
    find_executable(&["yt-dlp.exe", "yt-dlp"])
        .or_else(|| {
            Path::new(FALLBACK_YT_DLP)
                .is_file()
                .then(|| PathBuf::from(FALLBACK_YT_DLP))
        })
        .ok_or_else(|| "yt-dlp was not found".to_string())
}

fn ffmpeg() -> Result<PathBuf, String> {
    find_executable(&["ffmpeg.exe", "ffmpeg"]).ok_or_else(|| "ffmpeg was not found".to_string())
}

fn run(mut command: Command, label: &str) -> Result<String, String> {
    command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    command.creation_flags(CREATE_NO_WINDOW);
    let output = command
        .output()
        .map_err(|error| format!("Could not run {label}: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "{label} failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }
    Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
}

fn safe_name(value: &str, fallback: &str) -> String {
    let invalid = Regex::new(r#"[<>:"\\/|?*\x00-\x1f]"#).unwrap();
    let spaces = Regex::new(r"[\s_-]+").unwrap();
    let clean = invalid.replace_all(value, "");
    let clean = spaces.replace_all(clean.trim(), "_");
    let limited: String = clean.chars().take(80).collect();
    let limited = limited.trim_matches(['.', '_', '-']);
    if limited.is_empty() {
        fallback.to_string()
    } else {
        limited.to_string()
    }
}

fn youtube_drive_dir() -> PathBuf {
    std::env::var_os("YOUTUBE_DRIVE_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(YOUTUBE_DRIVE_DIR))
}

fn copy_directory(source: &Path, destination: &Path) -> Result<(), String> {
    fs::create_dir_all(destination)
        .map_err(|error| format!("Could not create Google Drive folder: {error}"))?;
    for entry in fs::read_dir(source)
        .map_err(|error| format!("Could not read completed video folder: {error}"))?
    {
        let entry =
            entry.map_err(|error| format!("Could not read completed video item: {error}"))?;
        let target = destination.join(entry.file_name());
        if entry
            .file_type()
            .map_err(|error| format!("Could not inspect completed video item: {error}"))?
            .is_dir()
        {
            copy_directory(&entry.path(), &target)?;
        } else {
            fs::copy(entry.path(), target)
                .map_err(|error| format!("Could not copy video to Google Drive: {error}"))?;
        }
    }
    Ok(())
}

fn move_to_google_drive(source: &Path) -> Result<PathBuf, String> {
    let drive = youtube_drive_dir();
    fs::create_dir_all(&drive).map_err(|error| {
        format!(
            "Could not access Google Drive at {}: {error}",
            drive.display()
        )
    })?;
    let folder_name = source
        .file_name()
        .ok_or_else(|| "Completed video folder has no name.".to_string())?;
    let mut destination = drive.join(folder_name);
    let mut suffix = 2;
    while destination.exists() {
        destination = drive.join(format!("{}_{suffix}", folder_name.to_string_lossy()));
        suffix += 1;
    }
    if fs::rename(source, &destination).is_err() {
        if let Err(error) = copy_directory(source, &destination) {
            let _ = fs::remove_dir_all(&destination);
            return Err(error);
        }
        fs::remove_dir_all(source).map_err(|error| {
            format!(
                "Video copied to Google Drive, but the local copy could not be removed: {error}"
            )
        })?;
    }
    Ok(destination)
}

fn cookie_file(cookies: &[Cookie]) -> Result<PathBuf, String> {
    let path = std::env::temp_dir().join(format!(
        "chapter-clipper-cookies-{}.txt",
        std::process::id()
    ));
    let mut lines = vec![
        "# Netscape HTTP Cookie File".to_string(),
        "# Native Messaging session".to_string(),
        String::new(),
    ];
    for cookie in cookies {
        let domain = cookie.domain.as_deref().unwrap_or(".youtube.com");
        lines.push(format!(
            "{}\t{}\t{}\t{}\t{}\t{}\t{}",
            domain,
            if domain.starts_with('.') {
                "TRUE"
            } else {
                "FALSE"
            },
            cookie.path.as_deref().unwrap_or("/"),
            if cookie.secure.unwrap_or(false) {
                "TRUE"
            } else {
                "FALSE"
            },
            cookie.expiration_date.unwrap_or(0.0).floor() as i64,
            cookie.name,
            cookie.value
        ));
    }
    fs::write(&path, lines.join("\n"))
        .map_err(|error| format!("Could not prepare YouTube cookies: {error}"))?;
    Ok(path)
}

fn process(request: Request) -> Result<Response, String> {
    if request.action == "ping" {
        return Ok(Response {
            success: true,
            error: None,
            output_dir: None,
            clips: vec![],
            ready: Some(yt_dlp().is_ok() && ffmpeg().is_ok()),
        });
    }
    if request.action != "process" {
        return Err("Unknown native-host action".to_string());
    }
    let url = request
        .url
        .ok_or_else(|| "Missing YouTube URL".to_string())?;
    let chapters = request.chapters.unwrap_or_default();
    if chapters.is_empty() {
        return Err("Select at least one chapter".to_string());
    }
    let cookie_path = request
        .cookies
        .as_deref()
        .filter(|items| !items.is_empty())
        .map(cookie_file)
        .transpose()?;
    let yt = yt_dlp()?;
    let mut metadata = Command::new(&yt);
    metadata.args([
        "--print",
        "%(title)s",
        "--skip-download",
        "--no-playlist",
        "--no-warnings",
    ]);
    if let Some(path) = &cookie_path {
        metadata.args(["--cookies", path.to_string_lossy().as_ref()]);
    }
    metadata.arg(&url);
    let title = run(metadata, "yt-dlp metadata")?
        .lines()
        .next()
        .unwrap_or("YouTube Video")
        .to_string();
    let base = dirs::video_dir()
        .unwrap_or_else(|| dirs::home_dir().unwrap_or_else(std::env::temp_dir))
        .join("Chapter Clipper")
        .join(safe_name(&title, "YouTube_Video"));
    let clips_dir = base.join("clips");
    fs::create_dir_all(&clips_dir)
        .map_err(|error| format!("Could not create output directory: {error}"))?;
    let mut download = Command::new(&yt);
    download
        .args([
            "--no-playlist",
            "--no-warnings",
            "-f",
            "bestvideo+bestaudio/best",
            "--merge-output-format",
            "mp4",
            "--print",
            "after_move:filepath",
            "-o",
        ])
        .arg(base.join("source.%(ext)s"));
    if let Some(path) = &cookie_path {
        download.args(["--cookies", path.to_string_lossy().as_ref()]);
    }
    download.arg(&url);
    let output = run(download, "yt-dlp download")?;
    let source = output
        .lines()
        .rev()
        .map(|line| PathBuf::from(line.trim()))
        .find(|path| path.is_file())
        .or_else(|| {
            fs::read_dir(&base)
                .ok()?
                .filter_map(Result::ok)
                .map(|item| item.path())
                .find(|path| {
                    path.extension()
                        .and_then(|value| value.to_str())
                        .is_some_and(|value| value.eq_ignore_ascii_case("mp4"))
                })
        })
        .ok_or_else(|| "Downloaded source file was not found".to_string())?;
    let ff = ffmpeg()?;
    let mut clips = Vec::new();
    for (position, chapter) in chapters.iter().enumerate() {
        let duration = chapter.end_time - chapter.start_time;
        if duration <= 0.5 || chapter.duration <= 0.0 {
            continue;
        }
        let index = if chapter.index > 0 {
            chapter.index
        } else {
            position + 1
        };
        let path = clips_dir.join(format!(
            "{index:02}_{}.mp4",
            safe_name(&chapter.title, "Chapter")
        ));
        let mut cut = Command::new(&ff);
        cut.args([
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-ss",
            &chapter.start_time.to_string(),
            "-i",
        ])
        .arg(&source)
        .args([
            "-t",
            &duration.to_string(),
            "-c:v",
            "libx264",
            "-crf",
            "18",
            "-preset",
            "fast",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
        ])
        .arg(&path);
        run(cut, &format!("ffmpeg clip {index}"))?;
        clips.push(Clip {
            index,
            title: chapter.title.clone(),
            file_path: path.to_string_lossy().into_owned(),
            duration,
        });
    }
    if let Some(path) = cookie_path {
        let _ = fs::remove_file(path);
    }
    let destination = move_to_google_drive(&base)?;
    for clip in &mut clips {
        let file_name = Path::new(&clip.file_path)
            .file_name()
            .ok_or_else(|| "Generated clip has no filename.".to_string())?;
        clip.file_path = destination
            .join("clips")
            .join(file_name)
            .to_string_lossy()
            .into_owned();
    }
    Ok(Response {
        success: true,
        error: None,
        output_dir: Some(destination.join("clips").to_string_lossy().into_owned()),
        clips,
        ready: None,
    })
}

fn read_message() -> io::Result<Option<Vec<u8>>> {
    let mut length = [0_u8; 4];
    match io::stdin().read_exact(&mut length) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::UnexpectedEof => return Ok(None),
        Err(error) => return Err(error),
    }
    let mut body = vec![0; u32::from_le_bytes(length) as usize];
    io::stdin().read_exact(&mut body)?;
    Ok(Some(body))
}

fn write_message(response: &Response) -> io::Result<()> {
    let body = serde_json::to_vec(response).map_err(io::Error::other)?;
    let mut stdout = io::stdout().lock();
    stdout.write_all(&(body.len() as u32).to_le_bytes())?;
    stdout.write_all(&body)?;
    stdout.flush()
}

fn main() {
    while let Ok(Some(body)) = read_message() {
        let response = match serde_json::from_slice::<Request>(&body)
            .map_err(|error| error.to_string())
            .and_then(process)
        {
            Ok(response) => response,
            Err(error) => Response {
                success: false,
                error: Some(error),
                output_dir: None,
                clips: vec![],
                ready: None,
            },
        };
        if write_message(&response).is_err() {
            break;
        }
    }
}
