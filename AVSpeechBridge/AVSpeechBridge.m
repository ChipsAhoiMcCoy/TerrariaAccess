#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <objc/runtime.h>
#import <string.h>
#import <dlfcn.h> // Needed for dladdr

// --- Configuration Structure ---

typedef struct {
    char voiceId[256];
    float rate;
} VoiceProvider;

// Global array to hold loaded providers
static VoiceProvider g_providers[100]; // Support up to 100 providers
static int g_providerCount = 0;
static bool g_configLoaded = false;

/**
 * Loads the voice provider configuration from a simple text file.
 * Expected format:
 * [Provider 1]
 * voiceId=com.apple.voice.compact.en-US.Samantha
 * rate=0.5
 * 
 * [Provider 2]
 * voiceId=com.apple.voice.compact.en-GB.Daniel
 * rate=0.6
 */
static void LoadConfiguration(void) {
    if (g_configLoaded) {
        return;
    }
    
    @autoreleasepool {
        // Get the path to the config file in the same directory as the dylib
        Dl_info info;
        if (dladdr((void*)LoadConfiguration, &info) == 0) {
            NSLog(@"Failed to get dylib path");
            g_configLoaded = true;
            return;
        }
        
        NSString *dylibPath = [NSString stringWithUTF8String:info.dli_fname];
        NSString *dylibDir = [dylibPath stringByDeletingLastPathComponent];
        NSString *configPath = [dylibDir stringByAppendingPathComponent:@"avspeechbridge.conf"];
        
        // Check if file exists, if not create a default one
        NSFileManager *fileManager = [NSFileManager defaultManager];
        if (![fileManager fileExistsAtPath:configPath]) {
            // Create default configuration
            NSString *defaultConfig = @"[Provider 1]\n"
                                      @"voiceId=com.apple.speech.synthesis.voice.Alex\n"
                                      @"rate=0.5\n"
                                      @"\n"
                                      @"[Provider 2]\n"
                                      @"voiceId=com.apple.voice.enhanced.en-GB.Kate\n"
                                      @"rate=0.5\n";
            
            NSError *error = nil;
            [defaultConfig writeToFile:configPath atomically:YES encoding:NSUTF8StringEncoding error:&error];
            
            if (error) {
                NSLog(@"Failed to create default config: %@", error);
            }
        }
        
        // Read and parse the configuration file
        NSError *error = nil;
        NSString *configContent = [NSString stringWithContentsOfFile:configPath 
                                                            encoding:NSUTF8StringEncoding 
                                                               error:&error];
        
        if (error || !configContent) {
            NSLog(@"Failed to read config file: %@", error);
            g_configLoaded = true;
            return;
        }
        
        NSArray *lines = [configContent componentsSeparatedByString:@"\n"];
        int currentProvider = -1;
        
        for (NSString *line in lines) {
            NSString *trimmed = [line stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
            
            if ([trimmed length] == 0) {
                continue; // Skip empty lines
            }
            
            // Check for provider header [Provider N]
            if ([trimmed hasPrefix:@"[Provider "] && [trimmed hasSuffix:@"]"]) {
                NSString *providerNum = [trimmed substringWithRange:NSMakeRange(10, [trimmed length] - 11)];
                int providerIndex = [providerNum intValue] - 1; // Convert to 0-based index
                
                if (providerIndex >= 0 && providerIndex < 100) {
                    currentProvider = providerIndex;
                    if (providerIndex >= g_providerCount) {
                        g_providerCount = providerIndex + 1;
                    }
                    // Initialize with defaults
                    strcpy(g_providers[providerIndex].voiceId, "");
                    g_providers[providerIndex].rate = 0.5f;
                }
                continue;
            }
            
            // Parse key=value pairs
            if (currentProvider >= 0 && [trimmed containsString:@"="]) {
                NSArray *parts = [trimmed componentsSeparatedByString:@"="];
                if ([parts count] == 2) {
                    NSString *key = [[parts objectAtIndex:0] stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
                    NSString *value = [[parts objectAtIndex:1] stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
                    
                    if ([key isEqualToString:@"voiceId"]) {
                        const char *voiceIdC = [value UTF8String];
                        if (voiceIdC) {
                            strncpy(g_providers[currentProvider].voiceId, voiceIdC, 255);
                            g_providers[currentProvider].voiceId[255] = '\0';
                        }
                    } else if ([key isEqualToString:@"rate"]) {
                        g_providers[currentProvider].rate = [value floatValue];
                    }
                }
            }
        }
    }
    
    g_configLoaded = true;
}

// --- Custom Configuration for SpeechSynthesizer ---

static const char *kSynthesizerConfigKey = "SRM_SynthesizerConfigKey";

@interface SRMSpeechConfig : NSObject
@property (nonatomic) float rate;
@property (nonatomic) float volume;
@property (nonatomic, copy) NSString *voiceIdentifier;
@end

@implementation SRMSpeechConfig
- (instancetype)init {
    self = [super init];
    if (self) {
        self.rate = 0.5f;
        self.volume = 1.0f;
        self.voiceIdentifier = nil;
    }
    return self;
}
@end

// --------------------------------------------------

// Helper to copy a C string that must be freed by the caller on the C# side
char* CopyStringToC(NSString* nsString) {
    if (nsString == nil) {
        return NULL;
    }
    @autoreleasepool {
        const char* cString = [nsString UTF8String];
        if (cString == NULL) {
            return NULL;
        }
        char* result = (char*)malloc(strlen(cString) + 1);
        if (result == NULL) {
            return NULL;
        }
        strcpy(result, cString);
        return result;
    }
}

// Bridge functions for AVSpeechSynthesizer

void* AVSpeechSynthesizer_New(int providerNumber) {
    @autoreleasepool {
        LoadConfiguration();
        
        AVSpeechSynthesizer *synthesizer = [[AVSpeechSynthesizer alloc] init];
        SRMSpeechConfig *config = [[SRMSpeechConfig alloc] init];
        
        // Load provider configuration (convert to 0-based index)
        int providerIndex = providerNumber - 1;
        if (providerIndex >= 0 && providerIndex < g_providerCount) {
            VoiceProvider *provider = &g_providers[providerIndex];
            
            if (strlen(provider->voiceId) > 0) {
                config.voiceIdentifier = [NSString stringWithUTF8String:provider->voiceId];
            }
            config.rate = provider->rate;
        }
        
        objc_setAssociatedObject(synthesizer, kSynthesizerConfigKey, config, OBJC_ASSOCIATION_RETAIN_NONATOMIC);
        
        return (__bridge_retained void*)synthesizer;
    }
}

void AVSpeechSynthesizer_Speak(void* synthesizerPtr, const char* text) {
    if (synthesizerPtr == NULL || text == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge AVSpeechSynthesizer*)synthesizerPtr;
        NSString *nsText = [NSString stringWithUTF8String:text];
        
        if (nsText != nil) {
            AVSpeechUtterance *utterance = [AVSpeechUtterance speechUtteranceWithString:nsText];
            SRMSpeechConfig *config = objc_getAssociatedObject(synthesizer, kSynthesizerConfigKey);

            if (config) {
                utterance.volume = config.volume;
                utterance.pitchMultiplier = 1.0;
                utterance.rate = config.rate;
                
                if (config.voiceIdentifier) {
                    AVSpeechSynthesisVoice *voice = [AVSpeechSynthesisVoice voiceWithIdentifier:config.voiceIdentifier];
                    if (voice) {
                        utterance.voice = voice;
                    }
                }
            }
            
            [synthesizer speakUtterance:utterance];
        }
    }
}

void AVSpeechSynthesizer_StopSpeaking(void* synthesizerPtr) {
    if (synthesizerPtr == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge AVSpeechSynthesizer*)synthesizerPtr;
        [synthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    }
}

void AVSpeechSynthesizer_Release(void* synthesizerPtr) {
    if (synthesizerPtr == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge_transfer AVSpeechSynthesizer*)synthesizerPtr;
        [synthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    }
}

// --- CONFIGURATION SETTER FUNCTIONS ---

void AVSpeechSynthesizer_SetRate(void* synthesizerPtr, float rate) {
    if (synthesizerPtr == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge AVSpeechSynthesizer*)synthesizerPtr;
        SRMSpeechConfig *config = objc_getAssociatedObject(synthesizer, kSynthesizerConfigKey);
        if (config) {
            config.rate = rate;
        }
    }
}

void AVSpeechSynthesizer_SetVolume(void* synthesizerPtr, float volume) {
    if (synthesizerPtr == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge AVSpeechSynthesizer*)synthesizerPtr;
        SRMSpeechConfig *config = objc_getAssociatedObject(synthesizer, kSynthesizerConfigKey);
        if (config) {
            config.volume = volume;
        }
    }
}

void AVSpeechSynthesizer_SetVoice(void* synthesizerPtr, const char* voiceIdentifier) {
    if (synthesizerPtr == NULL) {
        return;
    }
    
    @autoreleasepool {
        AVSpeechSynthesizer *synthesizer = (__bridge AVSpeechSynthesizer*)synthesizerPtr;
        SRMSpeechConfig *config = objc_getAssociatedObject(synthesizer, kSynthesizerConfigKey);
        
        if (!config) {
            return;
        }

        if (voiceIdentifier == NULL) {
            config.voiceIdentifier = nil;
            return;
        }

        NSString *nsVoiceId = [NSString stringWithUTF8String:voiceIdentifier];
        
        if (nsVoiceId) {
            AVSpeechSynthesisVoice *voice = [AVSpeechSynthesisVoice voiceWithIdentifier:nsVoiceId];
            if (voice) {
                config.voiceIdentifier = nsVoiceId;
            } else {
                config.voiceIdentifier = nil;
            }
        }
    }
}