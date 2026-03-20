Prompt Workshop is a fullstack web application that will help engineers write prompts and structured output object schemas to perform structured data extraction from documents (pdf, jpeg, png.)  

"As a USER, I will use the app `Prompt Workshop` to help write prompts and structured JSON schema in order to perform experiments and tests that are necessary to find best way to write instructions for various vision-capable Language Models that will be tasked to extract data from commercial or proof-of-sale documents. into structured JSON output." 

Features:  
- Projects: Create a project (provider + prompt + optional structured response JSON schema)
- Upload files to projects, store results
- Setup providers
  - name
  - model names (can be more than one)
  - base URL of API 
  - API KEY
  - Inference adapter 

User Flow:  
- User sets up an inference provider 
  - specify settings for `Model Names`, `Base URL`, `API` key and `Inference Adapter`
  - Values for `adapter`: 
    - `OpenAI API Compatible`
    - `Mistral OCR`
- User sets up a project 
  - Specifies prompt, structured response JSON  
    - prompt and JSON schema is edited via Monaco Editor (for line numbers and syntax highlighting support)
    - structured response JSON is `enabled` by default (can be toggled disabled by user to investigate inference response without expected structured object schema)
  - Chooses the provider for the project (can be changed whenever)
  - Uploads files, runs inference on each (or all) uploaded file/s

CONCEPT: `Inference Adapter`  

A Standard interface for submitting inference requests to different providers / models. 

To initialize an adapter, it needs the base URL, and the API key. Each adapter has an implementation: 
  - `OpenAI API Compatible` - Submits a request to the Chat Completions API. 
  - `Mistral OCR` - Submits a request to the Mistral OCR API for `document_annotation` as documented in `https://docs.mistral.ai/api/endpoint/ocr`

To submit a request, the user (through the UI) 
- provides the following to the adapter: 
  - File (as selected from uploaded files in the project)
  - Prompt + structured response object schema
- receives the structured response object from the adapter
- saves the received result (response object or error) in the file for user evaluation

Tech Stack:
- Tanstack with Shadcn
- sqlite (used to save providers, projects and their files and other metadata for persistence and portability to other machines.) 


