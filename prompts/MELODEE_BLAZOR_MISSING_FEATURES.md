Artist
  - Images 
    - Should display all images found in the artist folder
    - Ability to delete existing images and drag-n-drop new images
  - Relationships
    - Should display all artist (using AlbumDataInfoCardCompact) in artist name order
    - Ability to click "Add Relationship" and a model dialog appears
        - Select artist from a autocomplete of artists
        - Select relationship from a selectdion of relationships (ArtistRelationType enum has defined values)
Album
  - Files
    - Show show all files in the albums folder with the ability to delete files
 
User 
    - Implement /data/useredit/{apikey}
    - Be able to edit user record details
        - Use same layout like other edit screens like other edit (AlbumEdit, ArtistEdit)
    - Be able to set user role (none, Editor, Admin)
    - Be able to upload new user avatar
    - Be able to lock user 
